using System.Collections.Immutable;
using System.Linq;
using DecisionDriven.Analyzers.Rules;
using Microsoft.CodeAnalysis;
using Xunit;

namespace DecisionDriven.Analyzers.Tests.Rules;

/// <summary>
/// DD0001: a reference within a family points strictly downward.
/// </summary>
public sealed class LayerReferenceTests
{
    private const string Source = "internal sealed class Consumer { }";

    [Fact]
    public void A_reference_to_a_lower_layer_is_not_reported()
    {
        // The conforming sample. Without this the rule is a ban rather than a rule.
        ImmutableArray<Diagnostic> diagnostics = Run(
            layer: 1,
            new RuleHarness.Referenced("Sample.Layer0", archLayer: 0));

        Assert.Empty(diagnostics);
    }

    [Fact]
    public void A_reference_to_the_same_layer_is_reported()
    {
        ImmutableArray<Diagnostic> diagnostics = Run(
            layer: 1,
            new RuleHarness.Referenced("Sample.Sibling", archLayer: 1));

        Diagnostic diagnostic = Assert.Single(diagnostics);
        Assert.Equal("DD0001", diagnostic.Id);
        Assert.Equal(DiagnosticSeverity.Error, diagnostic.Severity);
        Assert.Contains("is at layer 1 and references 'Sample.Sibling', which is at layer 1", diagnostic.GetMessage(), System.StringComparison.Ordinal);
    }

    [Fact]
    public void A_reference_to_a_higher_layer_is_reported()
    {
        ImmutableArray<Diagnostic> diagnostics = Run(
            layer: 1,
            new RuleHarness.Referenced("Sample.Above", archLayer: 2));

        Assert.Equal("DD0001", Assert.Single(diagnostics).Id);
    }

    [Fact]
    public void A_family_reference_with_no_declared_layer_is_reported()
    {
        // An assembly nobody placed cannot be checked. Letting it through would make the rule
        // depend on whether somebody remembered to set a property, which is the same as not
        // having the rule.
        ImmutableArray<Diagnostic> diagnostics = Run(
            layer: 1,
            new RuleHarness.Referenced("Sample.Unplaced", archLayer: null));

        Diagnostic diagnostic = Assert.Single(diagnostics);
        Assert.Equal("DD0001", diagnostic.Id);
        Assert.Contains("declares no layer", diagnostic.GetMessage(), System.StringComparison.Ordinal);
    }

    [Fact]
    public void An_assembly_outside_the_family_is_ignored()
    {
        // Families are how two unrelated products share the package without their layer numbers
        // meaning anything to each other.
        ImmutableArray<Diagnostic> diagnostics = Run(
            layer: 1,
            new RuleHarness.Referenced("Other.Thing", archLayer: 5),
            new RuleHarness.Referenced("Unrelated", archLayer: null));

        Assert.Empty(diagnostics);
    }

    [Fact]
    public void An_assembly_whose_name_merely_starts_with_the_family_word_is_ignored()
    {
        // 'Sample' is the family; 'Samples.Other' is not in it. Matching the prefix without the
        // dot would put an unrelated product's assemblies under this project's layering.
        ImmutableArray<Diagnostic> diagnostics = Run(
            layer: 1,
            new RuleHarness.Referenced("Samples.Other", archLayer: 9));

        Assert.Empty(diagnostics);
    }

    [Fact]
    public void A_test_project_is_not_checked()
    {
        // Exempt from being checked, per StableDependencyRules.LayerReferenceStrictlyDownward: a
        // test reaches across layers on purpose. It still counts as a reference for others.
        ImmutableArray<Diagnostic> diagnostics = RuleHarness.Run(
            new LayerReferenceAnalyzer(),
            Source,
            assemblyName: "Sample.Layer1.Tests",
            archFamily: "Sample",
            archLayer: 1,
            references: new[] { new RuleHarness.Referenced("Sample.Layer1", archLayer: 1) });

        Assert.Empty(diagnostics);
    }

    [Fact]
    public void A_project_with_no_family_or_no_layer_is_not_checked()
    {
        // Unset is a real state: the project has not opted into layering. Reporting here would
        // make adopting the package a build break for every project nobody has placed yet.
        Assert.Empty(RuleHarness.Run(
            new LayerReferenceAnalyzer(),
            Source,
            archFamily: null,
            archLayer: 1,
            references: new[] { new RuleHarness.Referenced("Sample.Sibling", archLayer: 1) }));

        Assert.Empty(RuleHarness.Run(
            new LayerReferenceAnalyzer(),
            Source,
            archFamily: "Sample",
            archLayer: null,
            references: new[] { new RuleHarness.Referenced("Sample.Sibling", archLayer: 1) }));
    }

    [Fact]
    public void The_message_for_the_canonical_violating_sample_is_exact()
    {
        // DiagnosticMessages.ExactMessageTested. The message is the only channel that reaches a
        // reader of build output, so its wording is part of the rule and changes with a decision,
        // not with a refactor.
        ImmutableArray<Diagnostic> diagnostics = Run(
            layer: 1,
            new RuleHarness.Referenced("Sample.Sibling", archLayer: 1));

        Assert.Equal(
            "'Sample.Layer1' is at layer 1 and references 'Sample.Sibling', which is at layer 1. "
            + "Decide: move what 'Sample.Layer1' needs down to a layer below 1, or invert the dependency "
            + "so 'Sample.Sibling' does not have to be referenced from layer 1 "
            + "| mark it [DesignDecision(typeof(<Set>.<Key>), Scope = ExceptionScope.<Scope>)] "
            + "citing the accepted decision that says so. "
            + "Do not add the attribute without a decision that answers this; "
            + "if the reason is only that the code already looked like this, take the design change.",
            Assert.Single(diagnostics).GetMessage());
    }

    private static ImmutableArray<Diagnostic> Run(int layer, params RuleHarness.Referenced[] references) =>
        RuleHarness.Run(
            new LayerReferenceAnalyzer(),
            Source,
            assemblyName: "Sample.Layer1",
            archFamily: "Sample",
            archLayer: layer,
            references: references);
}
