using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using System.Threading;
using DecisionDriven.Analyzers.Tests.Ledger;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;

namespace DecisionDriven.Analyzers.Tests.Rules;

/// <summary>
/// Runs a rule over a compilation whose references are real assemblies.
/// </summary>
/// <remarks>
/// DD0001 reads <c>[ArchLayer]</c> out of a *referenced assembly's* metadata, so a source-only test
/// would be testing something the rule does not do. This harness compiles each referenced project
/// for real - generator and all, so the attribute is emitted the way a build emits it - and hands
/// the resulting image over as a <see cref="MetadataReference"/>.
/// </remarks>
internal static class RuleHarness
{
    /// <summary>A project to compile and reference.</summary>
    internal sealed class Referenced
    {
        internal Referenced(string assemblyName, int? archLayer, string source = "internal sealed class Marker { }")
        {
            AssemblyName = assemblyName;
            ArchLayer = archLayer;
            Source = source;
        }

        internal string AssemblyName { get; }

        /// <summary>Null means the project declares no layer, which DD0001 treats as an error to reference.</summary>
        internal int? ArchLayer { get; }

        internal string Source { get; }
    }

    /// <summary>
    /// Compiles <paramref name="references"/> to real images, then runs <paramref name="analyzer"/>
    /// over a compilation that references them.
    /// </summary>
    internal static ImmutableArray<Diagnostic> Run(
        DiagnosticAnalyzer analyzer,
        string source,
        string assemblyName = "Sample.Layer1",
        string? archFamily = null,
        int? archLayer = null,
        bool compositionRoot = false,
        string? contractTypeAssemblies = null,
        IEnumerable<Referenced>? references = null,
        Dictionary<string, string>? editorConfig = null)
    {
        List<MetadataReference> metadata = new List<MetadataReference>(PlatformReferences());

        foreach (Referenced reference in references ?? Array.Empty<Referenced>())
        {
            metadata.Add(Build(reference));
        }

        CSharpCompilation compilation = CSharpCompilation.Create(
            assemblyName,
            new[] { CSharpSyntaxTree.ParseText(source, new CSharpParseOptions(LanguageVersion.Latest)) },
            metadata,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, nullableContextOptions: NullableContextOptions.Enable));

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

        if (contractTypeAssemblies is not null)
        {
            options["build_property.ArchContractTypeAssemblies"] = contractTypeAssemblies;
        }

        // .editorconfig options arrive through the same provider as MSBuild properties, without
        // the build_property prefix.
        foreach (KeyValuePair<string, string> entry in editorConfig ?? new Dictionary<string, string>(StringComparer.Ordinal))
        {
            options[entry.Key] = entry.Value;
        }

        CompilationWithAnalyzers withAnalyzers = compilation.WithAnalyzers(
            ImmutableArray.Create(analyzer),
            new AnalyzerOptions(
                ImmutableArray<AdditionalText>.Empty,
                new GlobalOptionsProvider(options)));

        ImmutableArray<Diagnostic> diagnostics = withAnalyzers
            .GetAnalyzerDiagnosticsAsync(CancellationToken.None)
            .GetAwaiter()
            .GetResult();

        // A test that silently compiled broken source would pass for the wrong reason.
        ImmutableArray<Diagnostic> compileErrors = compilation.GetDiagnostics()
            .Where(d => d.Severity == DiagnosticSeverity.Error)
            .ToImmutableArray();

        if (!compileErrors.IsEmpty)
        {
            throw new InvalidOperationException(
                "The test source did not compile:" + Environment.NewLine
                + string.Join(Environment.NewLine, compileErrors.Select(d => d.ToString())));
        }

        return diagnostics;
    }

    /// <summary>
    /// Runs a rule over a compilation somebody else built - in practice, one the generator has
    /// already run over.
    /// </summary>
    /// <remarks>
    /// DD0007 is about the difference between a type the generator emitted and a type that merely
    /// looks like one, so its tests have to start from a real generator run. Building the
    /// compilation by hand here would test the analyzer against the harness's idea of generated
    /// code rather than against the generator's.
    /// </remarks>
    /// <param name="fileOptions">
    /// .editorconfig options by tree path, as a section covering that file would supply them.
    /// </param>
    internal static ImmutableArray<Diagnostic> RunOn(
        DiagnosticAnalyzer analyzer,
        Compilation compilation,
        Dictionary<string, string>? analyzerConfig = null,
        bool allowCompileErrors = false,
        Dictionary<string, Dictionary<string, string>>? fileOptions = null)
    {
        CompilationWithAnalyzers withAnalyzers = compilation.WithAnalyzers(
            ImmutableArray.Create(analyzer),
            new AnalyzerOptions(
                ImmutableArray<AdditionalText>.Empty,
                new GlobalOptionsProvider(analyzerConfig ?? new Dictionary<string, string>(StringComparer.Ordinal), fileOptions)));

        // One case is about a citation the compiler rejects on its own, so that test opts out of
        // the guard rather than the guard being dropped for every other one.
        ImmutableArray<Diagnostic> compileErrors = compilation.GetDiagnostics()
            .Where(d => d.Severity == DiagnosticSeverity.Error)
            .ToImmutableArray();

        if (!allowCompileErrors && !compileErrors.IsEmpty)
        {
            throw new InvalidOperationException(
                "The test source did not compile:" + Environment.NewLine
                + string.Join(Environment.NewLine, compileErrors.Select(d => d.ToString())));
        }

        return withAnalyzers
            .GetAnalyzerDiagnosticsAsync(CancellationToken.None)
            .GetAwaiter()
            .GetResult();
    }

    /// <summary>
    /// Compiles a referenced project the way a build would, so its <c>[ArchLayer]</c> is in metadata.
    /// </summary>
    internal static MetadataReference BuildReference(Referenced reference) => Build(reference);

    private static MetadataReference Build(Referenced reference)
    {
        List<SyntaxTree> trees = new List<SyntaxTree>
        {
            CSharpSyntaxTree.ParseText(reference.Source, new CSharpParseOptions(LanguageVersion.Latest)),
        };

        // The generator emits the attribute from the MSBuild property, into its own file. Doing the
        // same here keeps the test honest - an assembly attribute has to precede every other
        // declaration in its file, which is exactly why the generator gives it a file of its own.
        if (reference.ArchLayer is int layer)
        {
            trees.Add(CSharpSyntaxTree.ParseText(ArchLayerAttributeSource(layer), new CSharpParseOptions(LanguageVersion.Latest)));
        }

        CSharpCompilation compilation = CSharpCompilation.Create(
            reference.AssemblyName,
            trees,
            PlatformReferences(),
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        using MemoryStream stream = new MemoryStream();
        Microsoft.CodeAnalysis.Emit.EmitResult result = compilation.Emit(stream);

        if (!result.Success)
        {
            throw new InvalidOperationException(
                "The referenced assembly '" + reference.AssemblyName + "' did not compile:" + Environment.NewLine
                + string.Join(Environment.NewLine, result.Diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error).Select(d => d.ToString())));
        }

        stream.Position = 0;
        return MetadataReference.CreateFromStream(stream);
    }

    /// <summary>
    /// The attribute exactly as the generator emits it, so the rule is tested against what it will
    /// actually meet: an <c>internal</c> attribute class, private to each assembly, matched by name.
    /// </summary>
    private static string ArchLayerAttributeSource(int layer) =>
        "[assembly: global::DecisionDriven.ArchLayer(" + layer.ToString(System.Globalization.CultureInfo.InvariantCulture) + ")]"
        + Environment.NewLine
        + "namespace DecisionDriven { internal sealed class ArchLayerAttribute : global::System.Attribute { "
        + "public ArchLayerAttribute(int layer) { Layer = layer; } public int Layer { get; } } }";

    internal static IEnumerable<MetadataReference> PlatformReferences()
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

    /// <summary>The MSBuild properties a rule reads, as an options provider.</summary>
    internal static AnalyzerConfigOptionsProvider OptionsFor(Dictionary<string, string> values) => new GlobalOptionsProvider(values);

    private sealed class GlobalOptionsProvider : AnalyzerConfigOptionsProvider
    {
        private readonly Dictionary<string, Dictionary<string, string>>? fileOptions;

        internal GlobalOptionsProvider(
            Dictionary<string, string> values,
            Dictionary<string, Dictionary<string, string>>? fileOptions = null)
        {
            GlobalOptions = new Options(values);
            this.fileOptions = fileOptions;
        }

        public override AnalyzerConfigOptions GlobalOptions { get; }

        public override AnalyzerConfigOptions GetOptions(SyntaxTree tree) =>
            new Options(fileOptions is not null && fileOptions.TryGetValue(tree.FilePath, out Dictionary<string, string>? values)
                ? values
                : new Dictionary<string, string>(StringComparer.Ordinal));

        public override AnalyzerConfigOptions GetOptions(AdditionalText textFile) => new Options(new Dictionary<string, string>(StringComparer.Ordinal));

        private sealed class Options : AnalyzerConfigOptions
        {
            private readonly Dictionary<string, string> values;

            internal Options(Dictionary<string, string> values)
            {
                this.values = values;
            }

            public override bool TryGetValue(string key, out string value) => values.TryGetValue(key, out value!);
        }
    }
}
