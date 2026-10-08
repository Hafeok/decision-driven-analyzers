using System;
using System.Collections.Immutable;
using DecisionDriven.Analyzers.Rules;
using Microsoft.CodeAnalysis;
using Xunit;

namespace DecisionDriven.Analyzers.Tests.Rules;

/// <summary>
/// Which namespaces a <c>[DomainModel]</c> declaration makes model, read through DD0013.
/// </summary>
/// <remarks>
/// <c>DecisionsAsTypes.DomainModelIncludesSubNamespaces</c>. Every model rule asks the same question
/// of the same declaration, so it is tested once, through the rule with the plainest finding.
/// </remarks>
public sealed class DomainModelNamespaceTests
{
    private const string Sources =
        "namespace Consumer { public sealed class Term { public int Read() => 0; } }"
        + " namespace Consumer.Stores { public sealed class Store { public void Add(int count) { } } }";

    [Fact]
    public void By_default_a_prefix_takes_in_every_namespace_under_it()
    {
        ImmutableArray<Diagnostic> diagnostics = Run("[assembly: global::DecisionDriven.DomainModel(\"Consumer\", " + ContractSource.Decision + ")]");

        Assert.Equal(2, diagnostics.Length);
        Assert.Contains(diagnostics, d => d.GetMessage().Contains("'Store.Add'", StringComparison.Ordinal));
    }

    [Fact]
    public void IncludeSubNamespaces_true_is_the_default_said_out_loud()
    {
        ImmutableArray<Diagnostic> diagnostics = Run(
            "[assembly: global::DecisionDriven.DomainModel(\"Consumer\", " + ContractSource.Decision + ", IncludeSubNamespaces = true)]");

        Assert.Equal(2, diagnostics.Length);
    }

    [Fact]
    public void IncludeSubNamespaces_false_makes_only_the_named_namespace_model()
    {
        // A root-namespace model with a sibling below it that is not model: the store is outside.
        Diagnostic diagnostic = Assert.Single(Run(
            "[assembly: global::DecisionDriven.DomainModel(\"Consumer\", " + ContractSource.Decision + ", IncludeSubNamespaces = false)]"));

        // DiagnosticMessages.ExactMessageTested: the finding is the model type's, not the store's.
        Assert.Equal(
            "the return type of 'Term.Read' is 'int'. "
            + "Decide: give 'int' a name: a readonly record struct wrapping it, so that two of them "
            + "cannot be swapped and the surface says which is which "
            + "| mark it [DesignDecision(typeof(<Set>.<Key>), Scope = ExceptionScope.<Scope>)] "
            + "citing the accepted decision that says so. "
            + "Do not add the attribute without a decision that answers this; if the reason is only that "
            + "the code already looked like this, take the design change.",
            diagnostic.GetMessage());
    }

    [Fact]
    public void An_exact_declaration_and_a_prefix_declaration_combine()
    {
        ImmutableArray<Diagnostic> diagnostics = Run(
            "[assembly: global::DecisionDriven.DomainModel(\"Consumer\", " + ContractSource.Decision + ", IncludeSubNamespaces = false)]"
            + "[assembly: global::DecisionDriven.DomainModel(\"Consumer.Stores\", " + ContractSource.Decision + ")]");

        Assert.Equal(2, diagnostics.Length);
    }

    private static ImmutableArray<Diagnostic> Run(string assemblyAttributes) =>
        RuleHarness.Run(
            new NakedPrimitiveAnalyzer(),
            ContractSource.File(Sources, assemblyAttributes),
            assemblyName: "Consumer",
            archLayer: 1);
}
