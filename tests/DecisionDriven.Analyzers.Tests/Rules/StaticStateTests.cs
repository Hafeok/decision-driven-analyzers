using System;
using System.Collections.Immutable;
using System.Linq;
using DecisionDriven.Analyzers.Rules;
using Microsoft.CodeAnalysis;
using Xunit;

namespace DecisionDriven.Analyzers.Tests.Rules;

/// <summary>
/// DD0004: no mutable static state.
/// </summary>
public sealed class StaticStateTests
{
    /// <summary>
    /// The attributes as the generator emits them, so the exemption is tested against the real
    /// thing rather than against a stand-in with the same name.
    /// </summary>
    private const string Attributes = """
        namespace DecisionDriven
        {
            [global::System.AttributeUsage(global::System.AttributeTargets.All, AllowMultiple = true)]
            internal sealed class DesignDecisionAttribute : global::System.Attribute
            {
                public DesignDecisionAttribute(global::System.Type decision) { Decision = decision; }
                public global::System.Type Decision { get; }
                public ExceptionScope Scope { get; set; }
            }

            internal enum ExceptionScope { Boundary, HotPath, Pool, Interop, Compatibility, Migration }
        }
        """;

    [Fact]
    public void A_static_readonly_of_an_immutable_type_is_not_reported()
    {
        // The conforming sample. A static readonly string is the commonest static there is, and a
        // rule that reported it would be turned off on the first day.
        Assert.Empty(Run("""
            internal sealed class Thing
            {
                private static readonly string Name = "sample";
                private const int Limit = 10;
            }
            """));
    }

    [Fact]
    public void A_static_readonly_list_is_reported()
    {
        Diagnostic diagnostic = Assert.Single(Run("""
            internal sealed class Thing
            {
                private static readonly global::System.Collections.Generic.List<string> Items = new();
            }
            """));

        Assert.Equal("DD0004", diagnostic.Id);
        Assert.Equal(DiagnosticSeverity.Error, diagnostic.Severity);
        Assert.Contains("static registry", diagnostic.GetMessage(), StringComparison.Ordinal);
    }

    [Fact]
    public void A_pool_marked_with_DesignDecision_is_not_reported()
    {
        // StaticState.PoolsExemptByDesignDecision. Whether the cited decision exists and whether
        // Pool is the right scope is DD0007's question; this rule only asks whether the exception
        // was documented at all.
        Assert.Empty(Run(Attributes + """

            internal sealed class Thing
            {
                [global::DecisionDriven.DesignDecision(typeof(Thing), Scope = global::DecisionDriven.ExceptionScope.Pool)]
                private static readonly global::System.Buffers.ArrayPool<byte> Buffers = global::System.Buffers.ArrayPool<byte>.Shared;
            }
            """));
    }

    [Fact]
    public void A_static_collection_written_from_an_instance_member_is_reported()
    {
        // The registry: something adds to it and something else reads it, and neither appears in
        // the reference graph. It is reported for being a static mutable collection, which is what
        // a registry necessarily is - the write is what it is for, not what makes it one.
        Diagnostic diagnostic = Assert.Single(Run("""
            internal sealed class Registry
            {
                private static readonly global::System.Collections.Generic.Dictionary<string, object> Handlers = new();

                public void Register(string key, object handler) => Handlers[key] = handler;
            }
            """));

        Assert.Equal("DD0004", diagnostic.Id);
        Assert.Contains("static registry", diagnostic.GetMessage(), StringComparison.Ordinal);
    }

    [Fact]
    public void A_non_readonly_static_is_reported()
    {
        Diagnostic diagnostic = Assert.Single(Run("""
            internal sealed class Thing
            {
                private static int counter;
            }
            """));

        Assert.Contains("is not readonly", diagnostic.GetMessage(), StringComparison.Ordinal);
    }

    [Fact]
    public void A_ThreadStatic_is_reported()
    {
        Diagnostic diagnostic = Assert.Single(Run("""
            internal sealed class Thing
            {
                [global::System.ThreadStatic]
                private static string? current;
            }
            """));

        Assert.Contains("[ThreadStatic]", diagnostic.GetMessage(), StringComparison.Ordinal);
    }

    [Fact]
    public void An_AsyncLocal_static_is_reported()
    {
        Assert.Single(Run("""
            internal sealed class Thing
            {
                private static readonly global::System.Threading.AsyncLocal<string> Current = new();
            }
            """));
    }

    [Fact]
    public void A_static_property_with_a_setter_is_reported()
    {
        Diagnostic diagnostic = Assert.Single(Run("""
            internal sealed class Thing
            {
                public static string Name { get; set; } = "sample";
            }
            """));

        Assert.Contains("has a setter", diagnostic.GetMessage(), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("global::System.Text.RegularExpressions.Regex", "new(\"a\")")]
    [InlineData("global::System.Buffers.ArrayPool<byte>", "global::System.Buffers.ArrayPool<byte>.Shared")]
    [InlineData("global::System.Collections.Immutable.ImmutableArray<string>", "global::System.Collections.Immutable.ImmutableArray<string>.Empty")]
    public void The_types_ADR_A05_exempts_are_not_reported(string type, string initialiser)
    {
        // Named exemptions rather than a general rule, because each is famously shared on purpose
        // and an exemption on every use of one would say nothing.
        Assert.Empty(Run($$"""
            internal sealed class Thing
            {
                private static readonly {{type}} Value = {{initialiser}};
            }
            """));
    }

    [Fact]
    public void A_test_assembly_is_not_checked()
    {
        Assert.Empty(RuleHarness.Run(
            new StaticStateAnalyzer(),
            """
            internal sealed class Fixture
            {
                private static int counter;
            }
            """,
            assemblyName: "Sample.Layer1.Tests"));
    }

    [Fact]
    public void The_message_for_the_canonical_violating_sample_is_exact()
    {
        // DiagnosticMessages.ExactMessageTested.
        Diagnostic diagnostic = Assert.Single(Run("""
            internal sealed class Thing
            {
                private static readonly global::System.Collections.Generic.List<string> Items = new();
            }
            """));

        Assert.Equal(
            "static readonly field 'Thing.Items' holds a mutable collection "
            + "'System.Collections.Generic.List<string>', which is the shape of a static registry. "
            + "Decide: make it an immutable collection if it never changes, or give the collection to "
            + "the object that owns it and pass that object where it is needed "
            + "| mark it [DesignDecision(typeof(<Set>.<Key>), Scope = ExceptionScope.<Scope>)] "
            + "citing the accepted decision that says so. "
            + "Do not add the attribute without a decision that answers this; "
            + "if the reason is only that the code already looked like this, take the design change.",
            diagnostic.GetMessage());
    }

    private static ImmutableArray<Diagnostic> Run(string source) =>
        RuleHarness.Run(new StaticStateAnalyzer(), source, assemblyName: "Sample.Layer1");
}
