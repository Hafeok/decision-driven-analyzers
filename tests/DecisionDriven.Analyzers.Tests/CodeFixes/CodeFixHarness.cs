using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using System.Threading;
using DecisionDriven.Analyzers.Tests.Rules;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CodeActions;
using Microsoft.CodeAnalysis.CodeFixes;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Text;

namespace DecisionDriven.Analyzers.Tests.CodeFixes;

/// <summary>
/// Runs a code fix the way a host does: analyse, offer, apply.
/// </summary>
/// <remarks>
/// Built on a real <see cref="AdhocWorkspace"/> rather than on string surgery, because what is being
/// tested is partly that the fix produces a document a host can hand back - and because the fixed
/// source then has to be compiled to show it does not compile.
/// </remarks>
internal static class CodeFixHarness
{
    private const string ProjectName = "Sample.Layer1";

    /// <summary>The actions a provider offers for the first diagnostic the analyzer reports.</summary>
    internal static ImmutableArray<CodeAction> OfferedActions(
        DiagnosticAnalyzer analyzer,
        CodeFixProvider provider,
        string source,
        string assemblyName = ProjectName,
        string? archFamily = null,
        int? archLayer = null,
        bool compositionRoot = false,
        IEnumerable<RuleHarness.Referenced>? references = null)
    {
        (Document document, ImmutableArray<Diagnostic> diagnostics) = Analyse(
            analyzer, source, assemblyName, archFamily, archLayer, compositionRoot, references);

        if (diagnostics.IsEmpty)
        {
            throw new InvalidOperationException("The analyzer reported nothing, so there is no fix to offer.");
        }

        List<CodeAction> actions = new List<CodeAction>();

        foreach (Diagnostic diagnostic in diagnostics)
        {
            CodeFixContext context = new CodeFixContext(
                document,
                diagnostic,
                (action, _) => actions.Add(action),
                CancellationToken.None);

            provider.RegisterCodeFixesAsync(context).GetAwaiter().GetResult();
        }

        return actions.ToImmutableArray();
    }

    /// <summary>Applies the single offered action and returns the resulting source.</summary>
    internal static string ApplySingle(
        DiagnosticAnalyzer analyzer,
        CodeFixProvider provider,
        string source,
        string assemblyName = ProjectName,
        bool compositionRoot = false)
    {
        (Document document, ImmutableArray<Diagnostic> diagnostics) = Analyse(
            analyzer, source, assemblyName, archFamily: null, archLayer: null, compositionRoot, references: null);

        Diagnostic diagnostic = diagnostics.Single();

        List<CodeAction> actions = new List<CodeAction>();
        CodeFixContext context = new CodeFixContext(
            document,
            diagnostic,
            (action, _) => actions.Add(action),
            CancellationToken.None);

        provider.RegisterCodeFixesAsync(context).GetAwaiter().GetResult();

        CodeAction action = actions.Single();

        ImmutableArray<CodeActionOperation> operations = action
            .GetOperationsAsync(CancellationToken.None)
            .GetAwaiter()
            .GetResult();

        Solution changed = operations
            .OfType<ApplyChangesOperation>()
            .Single()
            .ChangedSolution;

        SourceText text = changed.GetDocument(document.Id)!
            .GetTextAsync(CancellationToken.None)
            .GetAwaiter()
            .GetResult();

        return text.ToString();
    }

    /// <summary>
    /// Compiles source on its own and returns the errors.
    /// </summary>
    /// <remarks>
    /// The attributes the placeholder names are declared here, so that the only thing that can fail
    /// to compile is the placeholder itself. Without them the test would pass because
    /// <c>DesignDecision</c> was undefined, which is not the claim.
    /// </remarks>
    internal static ImmutableArray<Diagnostic> CompileErrors(string source)
    {
        const string Attributes = """
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

        CSharpCompilation compilation = CSharpCompilation.Create(
            "FixedSource",
            new[]
            {
                CSharpSyntaxTree.ParseText(source, new CSharpParseOptions(LanguageVersion.Latest)),
                CSharpSyntaxTree.ParseText(Attributes, new CSharpParseOptions(LanguageVersion.Latest)),
            },
            PlatformReferences(),
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, nullableContextOptions: NullableContextOptions.Enable));

        return compilation.GetDiagnostics()
            .Where(static d => d.Severity == DiagnosticSeverity.Error)
            .ToImmutableArray();
    }

    private static (Document Document, ImmutableArray<Diagnostic> Diagnostics) Analyse(
        DiagnosticAnalyzer analyzer,
        string source,
        string assemblyName,
        string? archFamily,
        int? archLayer,
        bool compositionRoot,
        IEnumerable<RuleHarness.Referenced>? references)
    {
        AdhocWorkspace workspace = new AdhocWorkspace();

        ProjectId projectId = ProjectId.CreateNewId(assemblyName);
        Solution solution = workspace.CurrentSolution
            .AddProject(projectId, assemblyName, assemblyName, LanguageNames.CSharp)
            .WithProjectCompilationOptions(projectId, new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, nullableContextOptions: NullableContextOptions.Enable))
            .WithProjectParseOptions(projectId, new CSharpParseOptions(LanguageVersion.Latest))
            .AddMetadataReferences(projectId, PlatformReferences());

        foreach (RuleHarness.Referenced reference in references ?? Array.Empty<RuleHarness.Referenced>())
        {
            solution = solution.AddMetadataReference(projectId, RuleHarness.BuildReference(reference));
        }

        DocumentId documentId = DocumentId.CreateNewId(projectId);
        solution = solution.AddDocument(documentId, "Source.cs", SourceText.From(source));

        Project project = solution.GetProject(projectId)!;
        Compilation compilation = project.GetCompilationAsync(CancellationToken.None).GetAwaiter().GetResult()!;

        Dictionary<string, string> options = new Dictionary<string, string>(StringComparer.Ordinal);
        if (archFamily is not null)
        {
            options["build_property.ArchFamily"] = archFamily;
        }

        if (archLayer is int layer)
        {
            options["build_property.ArchLayer"] = layer.ToString(System.Globalization.CultureInfo.InvariantCulture);
        }

        if (compositionRoot)
        {
            options["build_property.ArchCompositionRoot"] = "true";
        }

        ImmutableArray<Diagnostic> diagnostics = compilation
            .WithAnalyzers(
                ImmutableArray.Create(analyzer),
                new AnalyzerOptions(ImmutableArray<AdditionalText>.Empty, RuleHarness.OptionsFor(options)))
            .GetAnalyzerDiagnosticsAsync(CancellationToken.None)
            .GetAwaiter()
            .GetResult();

        return (project.GetDocument(documentId)!, diagnostics);
    }

    private static IEnumerable<MetadataReference> PlatformReferences()
    {
        string trusted = (string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!;

        foreach (string path in trusted.Split(Path.PathSeparator))
        {
            if (path.EndsWith(".dll", StringComparison.OrdinalIgnoreCase))
            {
                yield return MetadataReference.CreateFromFile(path);
            }
        }
    }
}
