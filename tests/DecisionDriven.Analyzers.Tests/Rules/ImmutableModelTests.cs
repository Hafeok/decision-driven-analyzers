using System;
using System.Collections.Immutable;
using DecisionDriven.Analyzers.Rules;
using Microsoft.CodeAnalysis;
using Xunit;

namespace DecisionDriven.Analyzers.Tests.Rules;

/// <summary>
/// DD0019: a value handed out by a read cannot be changed by whoever got it.
/// </summary>
public sealed class ImmutableModelTests
{
    [Theory]
    [InlineData("public sealed class Quad { public int Count { get; set; } }", "has a public setter")]
    [InlineData("public sealed class Quad { public int Count { get; internal set; } }", "has an internal setter")]
    [InlineData("public sealed class Quad { public int Count; }", "is a field that is not readonly")]
    [InlineData("public sealed class Quad { public System.Collections.Generic.List<int> Items { get; } = new(); }", "which the caller can add to and remove from")]
    [InlineData("public sealed class Quad { public int[] Items { get; } = new int[0]; }", "which the caller can add to and remove from")]
    [InlineData("public sealed class Quad { public System.Collections.Generic.IList<int> Items { get; } = null!; }", "which the caller can add to and remove from")]
    [InlineData("public struct Quad { public int Count { get; } }", "is a model struct and is not readonly")]
    public void A_mutable_model_type_is_reported(string body, string expected)
    {
        Diagnostic diagnostic = Assert.Single(Run(body));

        Assert.Equal("DD0019", diagnostic.Id);
        Assert.Equal(DiagnosticSeverity.Error, diagnostic.Severity);
        Assert.Contains(expected, diagnostic.GetMessage(), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("public sealed class Quad { public int Count { get; init; } }")]
    [InlineData("public sealed class Quad { public readonly int Count; }")]
    [InlineData("public sealed class Quad { public System.Collections.Immutable.ImmutableArray<int> Items { get; init; } }")]
    [InlineData("public sealed class Quad { public System.Collections.Generic.IReadOnlyList<int> Items { get; init; } = null!; }")]
    [InlineData("public sealed class Quad { public System.ReadOnlyMemory<int> Items { get; init; } }")]
    [InlineData("public sealed class Quad { public System.Collections.Frozen.FrozenDictionary<int, int> Items { get; init; } = null!; }")]
    [InlineData("public readonly record struct Quad(int Count);")]
    public void An_immutable_model_type_is_not_reported(string body)
    {
        Assert.Empty(Run(body));
    }

    [Fact]
    public void A_sealed_builder_in_the_same_namespace_is_not_reported()
    {
        // ImmutableModel.BuildersAreTheEscapeHatch. Mutation while a value is being assembled is
        // fine; what is not fine is the value staying mutable after it is handed over.
        Assert.Empty(Run(
            "public sealed class QuadBuilder { public int Count { get; set; } "
            + "public System.Collections.Generic.List<int> Items { get; } = new(); }"));
    }

    [Fact]
    public void A_builder_that_is_not_sealed_is_reported()
    {
        Diagnostic diagnostic = Assert.Single(Run(
            "public class QuadBuilder { public int Count { get; set; } }"));

        Assert.Contains("is a builder and is not sealed", diagnostic.GetMessage(), StringComparison.Ordinal);
    }

    [Fact]
    public void A_builder_on_a_contract_signature_is_reported_by_DD0010()
    {
        // ADR-A11 says DD0010 already prevents this "since a builder is neither model vocabulary
        // nor a contract". It did not: a builder is in a [DomainModel] namespace by that same ADR,
        // so the namespace alone let it through. DD0010 now excludes builders from that allowance.
        Diagnostic diagnostic = Assert.Single(RuleHarness.Run(
            new ContractVocabularyAnalyzer(),
            File(
                "namespace Consumer.Model { public sealed class QuadBuilder { public int Count { get; set; } } }"
                + Environment.NewLine
                + "namespace Consumer { " + ContractSource.Contract
                + " public interface IQuadSource { global::Consumer.Model.QuadBuilder Read(); } }"),
            assemblyName: "Consumer",
            archLayer: 1));

        Assert.Equal("DD0010", diagnostic.Id);
        Assert.Contains(
            "is a builder, which is mutable by design and belongs to the code assembling a value",
            diagnostic.GetMessage(),
            StringComparison.Ordinal);
    }

    [Fact]
    public void A_type_outside_the_model_is_not_checked()
    {
        Assert.Empty(RuleHarness.Run(
            new MutableModelAnalyzer(),
            File("namespace Consumer.Scratch { public sealed class Quad { public int Count { get; set; } } }"),
            assemblyName: "Consumer",
            archLayer: 1));
    }

    [Fact]
    public void An_internal_model_type_is_not_checked()
    {
        // Not handed out, so nobody outside holds one to change.
        Assert.Empty(Run("internal sealed class Quad { public int Count { get; set; } }"));
    }

    [Fact]
    public void The_message_is_exactly_this()
    {
        // DiagnosticMessages.ExactMessageTested.
        Assert.Equal(
            "'Consumer.Model.Quad.Count' has a public setter. "
            + "Decide: make it 'init', or take the setter off and set the value in the constructor "
            + "| mark it [DesignDecision(typeof(<Set>.<Key>), Scope = ExceptionScope.<Scope>)] "
            + "citing the accepted decision that says so. "
            + "Do not add the attribute without a decision that answers this; if the reason is only that "
            + "the code already looked like this, take the design change.",
            Assert.Single(Run("public sealed class Quad { public int Count { get; set; } }")).GetMessage());
    }

    private static ImmutableArray<Diagnostic> Run(string body) =>
        RuleHarness.Run(
            new MutableModelAnalyzer(),
            File("namespace Consumer.Model { " + body + " }"),
            assemblyName: "Consumer",
            archLayer: 1);

    private static string File(string source) =>
        ContractSource.File(
            source,
            "[assembly: global::DecisionDriven.DomainModel(\"Consumer.Model\", " + ContractSource.Decision + ")]");
}
