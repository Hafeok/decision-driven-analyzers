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
        IEnumerable<Referenced>? references = null)
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
    /// Compiles a referenced project the way a build would, so its <c>[ArchLayer]</c> is in metadata.
    /// </summary>
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

    private sealed class GlobalOptionsProvider : AnalyzerConfigOptionsProvider
    {
        internal GlobalOptionsProvider(Dictionary<string, string> values)
        {
            GlobalOptions = new Options(values);
        }

        public override AnalyzerConfigOptions GlobalOptions { get; }

        public override AnalyzerConfigOptions GetOptions(SyntaxTree tree) => new Options(new Dictionary<string, string>(StringComparer.Ordinal));

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
