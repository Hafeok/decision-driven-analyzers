using System;
using System.Collections.Immutable;
using System.Linq;
using DecisionDriven.Analyzers.Rules;
using Microsoft.CodeAnalysis;
using Xunit;

namespace DecisionDriven.Analyzers.Tests.Rules;

/// <summary>
/// DD0020: no <c>dynamic</c> in a layered project.
/// </summary>
public sealed class DynamicBindingTests
{
    [Fact]
    public void A_local_typed_dynamic_and_a_call_bound_through_it_are_each_reported_once()
    {
        // The proposal's violating sample: the declaration and the call, one finding each.
        ImmutableArray<Diagnostic> diagnostics = Run("""
            namespace Sample.Core
            {
                public static class Relay
                {
                    public static void Forward(object handler, string message)
                    {
                        dynamic target = handler;
                        target.Handle(message);
                    }
                }
            }
            """);

        Assert.Equal(2, diagnostics.Length);
        Assert.All(diagnostics, d => Assert.Equal("DD0020", d.Id));
        Assert.All(diagnostics, d => Assert.Equal(DiagnosticSeverity.Error, d.Severity));
        Assert.Contains(diagnostics, d => d.GetMessage().Contains("uses 'dynamic' as a type", StringComparison.Ordinal));
        Assert.Contains(diagnostics, d => d.GetMessage().Contains("'target.Handle(message)' in 'Relay.Forward' is bound at run time", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("public static dynamic Wrap(object o) => o;")]
    [InlineData("public static void Take(dynamic value) { }")]
    [InlineData("private static readonly dynamic Held = new object();")]
    [InlineData("public static object Cast(object o) => (dynamic)o;")]
    [InlineData("public static System.Collections.Generic.List<dynamic> Many() => new();")]
    public void Dynamic_as_a_declared_type_is_reported(string member)
    {
        Diagnostic diagnostic = Assert.Single(Run("namespace Sample.Core { public static class Relay { " + member + " } }"));

        Assert.Contains("uses 'dynamic' as a type", diagnostic.GetMessage(), StringComparison.Ordinal);
    }

    [Fact]
    public void A_call_bound_at_run_time_on_a_value_declared_dynamic_elsewhere_is_reported()
    {
        // The value's type was written in another member; this one only uses it, and the use is the
        // finding here. A chain is one finding, not one per dot.
        ImmutableArray<Diagnostic> diagnostics = Run("""
            namespace Sample.Core
            {
                public static class Relay
                {
                    private static dynamic Source() => new object();

                    public static void Use()
                    {
                        Source().Next.Handle(1);
                        _ = Source()[0];
                    }
                }
            }
            """);

        Assert.Equal(3, diagnostics.Length);
        Assert.Single(diagnostics, d => d.GetMessage().Contains("'Source().Next.Handle(1)' in 'Relay.Use'", StringComparison.Ordinal));
        Assert.Single(diagnostics, d => d.GetMessage().Contains("'Source()[0]' in 'Relay.Use'", StringComparison.Ordinal));
    }

    [Fact]
    public void A_statically_bound_call_is_not_reported()
    {
        // The proposal's conforming sample.
        Assert.Empty(Run("""
            namespace Sample.Core
            {
                public interface IHandler { void Handle(string message); }

                public static class Relay
                {
                    public static void Forward(object handler, string message)
                    {
                        if (handler is IHandler target)
                        {
                            target.Handle(message);
                        }

                        string dynamic = message;
                        _ = dynamic.Length;
                    }
                }
            }
            """));
    }

    [Fact]
    public void A_project_with_no_layer_is_not_checked()
    {
        Assert.Empty(RuleHarness.Run(
            new DynamicBindingAnalyzer(),
            "namespace Sample.Core { public static class Relay { public static dynamic Wrap(object o) => o; } }",
            assemblyName: "Sample.Core"));
    }

    [Fact]
    public void A_member_or_type_marked_with_DesignDecision_is_not_reported()
    {
        Assert.Empty(Run(ContractSource.File("""
            namespace Sample.Core
            {
                public static class Interop
                {
                    [global::DecisionDriven.DesignDecision(typeof(object), Scope = global::DecisionDriven.ExceptionScope.Interop)]
                    public static void Call(object com) { dynamic app = com; app.Quit(); }
                }

                [global::DecisionDriven.DesignDecision(typeof(object), Scope = global::DecisionDriven.ExceptionScope.Interop)]
                public static class Scripting
                {
                    public static dynamic Evaluate(object host) => host;
                }
            }
            """)));
    }

    [Fact]
    public void The_message_is_exactly_this()
    {
        // DiagnosticMessages.ExactMessageTested.
        Assert.Equal(
            "'Relay.Wrap' uses 'dynamic' as a type, so what a value of it can do is decided at run time. "
            + "Decide: type the value as the contract it is used through, and test for that with 'is', so the compiler binds the call "
            + "| mark it [DesignDecision(typeof(<Set>.<Key>), Scope = ExceptionScope.<Scope>)] "
            + "citing the accepted decision that says so. "
            + "Do not add the attribute without a decision that answers this; if the reason is only that "
            + "the code already looked like this, take the design change.",
            Assert.Single(Run("namespace Sample.Core { public static class Relay { public static dynamic Wrap(object o) => o; } }")).GetMessage());
    }

    private static ImmutableArray<Diagnostic> Run(string source) =>
        RuleHarness.Run(new DynamicBindingAnalyzer(), source, assemblyName: "Sample.Core", archLayer: 1);
}
