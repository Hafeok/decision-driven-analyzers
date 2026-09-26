using System.IO;
using System.Linq;
using Microsoft.CodeAnalysis;
using Xunit;

namespace DecisionDriven.Analyzers.Tests.Ledger;

/// <summary>
/// Two compilations that both carry the generated types, one able to see the other's internals.
/// </summary>
/// <remarks>
/// <c>DecisionsAsTypes.AttributesAreSourceGenerated</c> puts an <c>internal</c> copy of every
/// generated type into every compilation, and <c>StableDependencyRules.InternalsVisibleToTestsOnly</c>
/// permits a library to show its internals to its test assembly. Together they meant a test
/// assembly saw two <c>DecisionDriven.ExceptionScope</c> types, its own and its library's, and the
/// compiler warned CS0436 on every use in the generated source, an error under warnings as errors.
/// The same held for the decision types the moment a test cited one.
/// </remarks>
public sealed class InternalsVisibleToTests
{
    private const string Library = @"
[assembly: System.Runtime.CompilerServices.InternalsVisibleTo(""Sample.Core.Tests"")]

namespace Sample.Core
{
    [DecisionDriven.DesignDecision(typeof(DecisionDriven.Ledger.SampleNs.SampleSet.SampleKey), Scope = DecisionDriven.ExceptionScope.Boundary)]
    public sealed class Widget { }
}";

    private const string TestAssembly = @"
namespace Sample.Core.Tests
{
    [DecisionDriven.DesignDecision(typeof(DecisionDriven.Ledger.SampleNs.SampleSet.SampleKey), Scope = DecisionDriven.ExceptionScope.Boundary)]
    public sealed class WidgetTests
    {
        public Sample.Core.Widget Subject { get; } = new Sample.Core.Widget();
    }
}";

    [Fact]
    public void A_test_assembly_that_sees_its_librarys_internals_compiles_without_type_conflicts()
    {
        GeneratorHarness.LedgerFile[] ledger = { LedgerInput.AsSet(LedgerInput.FrontMatter("sample-set", "SampleKey", "A statement.", acceptedBy: "mailto:someone@example.com")) };

        GeneratorHarness.Result library = GeneratorHarness.Run(Library, ledger, assemblyName: "Sample.Core");
        Assert.Empty(library.CompilationDiagnostics.Where(d => d.Severity == DiagnosticSeverity.Error));

        MetadataReference libraryReference = EmitReference(library.Compilation);

        GeneratorHarness.Result tests = GeneratorHarness.Run(
            TestAssembly,
            ledger,
            assemblyName: "Sample.Core.Tests",
            references: new[] { libraryReference });

        // CS0436 is the conflict with an imported type; it is a warning, and an error in any
        // consumer that builds with warnings as errors, which is every consumer this package targets.
        Assert.Empty(tests.CompilationDiagnostics.Where(d => d.Id == "CS0436"));
        Assert.Empty(tests.CompilationDiagnostics.Where(d => d.Severity == DiagnosticSeverity.Error));
    }

    [Fact]
    public void The_generated_types_are_hidden_from_other_compilations()
    {
        // The mechanism, stated directly: a referencing compilation cannot find the library's copy.
        GeneratorHarness.Result library = GeneratorHarness.Run(
            Library,
            new[] { LedgerInput.AsSet(LedgerInput.FrontMatter("sample-set", "SampleKey", "A statement.")) },
            assemblyName: "Sample.Core");

        // A referencing compilation with no generated types of its own, that can see the library's
        // internals: if embedding did not hide them, it would find them.
        Compilation reader = Microsoft.CodeAnalysis.CSharp.CSharpCompilation.Create(
            "Sample.Core.Tests",
            references: new[] { EmitReference(library.Compilation) }.Concat(library.Compilation.References));

        Assert.Null(reader.GetTypeByMetadataName("DecisionDriven.ExceptionScope"));
        Assert.Null(reader.GetTypeByMetadataName("DecisionDriven.DesignDecisionAttribute"));
        Assert.Null(reader.GetTypeByMetadataName("DecisionDriven.Ledger.SampleNs.SampleSet"));
    }

    private static MetadataReference EmitReference(Compilation compilation)
    {
        using MemoryStream stream = new MemoryStream();
        Microsoft.CodeAnalysis.Emit.EmitResult result = compilation.Emit(stream);
        Assert.True(result.Success, string.Join("\n", result.Diagnostics));
        return MetadataReference.CreateFromImage(stream.ToArray());
    }
}
