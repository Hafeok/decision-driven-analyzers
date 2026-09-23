using System.Linq;
using Microsoft.CodeAnalysis;
using Xunit;

namespace DecisionDriven.Analyzers.Tests.Ledger;

/// <summary>
/// The assembly's layer has to survive into metadata, because that is where DD0001 will read it from.
/// </summary>
public sealed class ArchLayerTests
{
    private const string Empty = "internal sealed class Nothing { }";

    [Fact]
    public void A_consumer_with_ArchLayer_two_carries_the_attribute_in_its_metadata()
    {
        GeneratorHarness.Result result = GeneratorHarness.Run(
            Empty,
            new[] { LedgerInput.AsSet(LedgerInput.FrontMatter("sample-set", "SomeKey", "A statement.")) },
            archLayer: "2");

        // Emitted and read back as metadata rather than read off the source compilation. DD0001
        // asks a *referenced* assembly what layer it is, so a test that only inspected the syntax
        // tree would pass while the rule it exists for still had nothing to read.
        IAssemblySymbol assembly = GeneratorHarness.EmitAndReadMetadata(result.Compilation);

        AttributeData attribute = Assert.Single(
            assembly.GetAttributes().Where(a => a.AttributeClass?.Name == "ArchLayerAttribute"));

        object? layer = Assert.Single(attribute.ConstructorArguments).Value;
        Assert.Equal(2, layer);
    }

    [Fact]
    public void A_consumer_without_ArchLayer_carries_no_attribute()
    {
        // An unset property means unlayered, which is a real state a rule has to distinguish from
        // layer 0. Emitting a default here would erase that distinction.
        GeneratorHarness.Result result = GeneratorHarness.Run(
            Empty,
            new[] { LedgerInput.AsSet(LedgerInput.FrontMatter("sample-set", "SomeKey", "A statement.")) });

        IAssemblySymbol assembly = GeneratorHarness.EmitAndReadMetadata(result.Compilation);

        Assert.Empty(assembly.GetAttributes().Where(a => a.AttributeClass?.Name == "ArchLayerAttribute"));
    }

    [Fact]
    public void A_non_integer_ArchLayer_leaves_the_assembly_unlayered()
    {
        GeneratorHarness.Result result = GeneratorHarness.Run(
            Empty,
            new[] { LedgerInput.AsSet(LedgerInput.FrontMatter("sample-set", "SomeKey", "A statement.")) },
            archLayer: "not-a-number");

        IAssemblySymbol assembly = GeneratorHarness.EmitAndReadMetadata(result.Compilation);

        Assert.Empty(assembly.GetAttributes().Where(a => a.AttributeClass?.Name == "ArchLayerAttribute"));
    }

    [Fact]
    public void The_marker_attributes_are_emitted_into_the_consumer()
    {
        // DecisionsAsTypes.AttributesAreSourceGenerated: every consumer gets its own internal copy,
        // so nothing here is a runtime dependency and nothing appears on a consumer's public surface.
        GeneratorHarness.Result result = GeneratorHarness.Run(
            Empty,
            new[] { LedgerInput.AsSet(LedgerInput.FrontMatter("sample-set", "SomeKey", "A statement.")) });

        string? source = result.GeneratedSource("DecisionDriven.Attributes.g.cs");
        Assert.NotNull(source);

        foreach (string name in new[]
        {
            "ArchLayerAttribute",
            "ContractAttribute",
            "DomainModelAttribute",
            "HotPathAttribute",
            "DesignDecisionAttribute",
            "enum ExceptionScope",
        })
        {
            Assert.Contains(name, source, System.StringComparison.Ordinal);
        }

        Assert.Empty(result.CompilationDiagnostics.Where(d => d.Severity == DiagnosticSeverity.Error));
    }

    [Fact]
    public void ExceptionScope_is_closed_to_the_six_named_values()
    {
        // DecisionsAsTypes.ExceptionScopeIsClosed. A seventh value added without a decision is the
        // failure this guards: the enum mirrors a SKOS scheme in the ledger, and the report tool
        // emits the same tokens.
        GeneratorHarness.Result result = GeneratorHarness.Run(
            "internal sealed class Nothing { }",
            new[] { LedgerInput.AsSet(LedgerInput.FrontMatter("sample-set", "SomeKey", "A statement.")) });

        INamedTypeSymbol? scope = result.Compilation.GetTypeByMetadataName("DecisionDriven.ExceptionScope");
        Assert.NotNull(scope);

        string[] values = scope!.GetMembers()
            .OfType<IFieldSymbol>()
            .Where(f => f.HasConstantValue)
            .Select(f => f.Name)
            .OrderBy(n => n, System.StringComparer.Ordinal)
            .ToArray();

        Assert.Equal(
            new[] { "Boundary", "Compatibility", "HotPath", "Interop", "Migration", "Pool" },
            values);
    }
}
