using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using System.Threading;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Text;

namespace DecisionDriven.Analyzers.Tests.Ledger;

/// <summary>
/// Runs the decision ledger generator over in-memory input.
/// </summary>
/// <remarks>
/// The generator's inputs are AdditionalFiles carrying <c>DdLedger</c> metadata and the
/// <c>ArchLayer</c> MSBuild property. Both arrive through <see cref="AnalyzerConfigOptions"/> in a
/// real build, so the harness supplies them the same way rather than reaching into the generator.
/// </remarks>
internal static class GeneratorHarness
{
    /// <summary>What a run produced.</summary>
    internal sealed class Result
    {
        internal Result(Compilation compilation, ImmutableArray<Diagnostic> generatorDiagnostics)
        {
            Compilation = compilation;
            GeneratorDiagnostics = generatorDiagnostics;
        }

        /// <summary>The compilation with the generated sources added.</summary>
        internal Compilation Compilation { get; }

        /// <summary>What the generator itself reported.</summary>
        internal ImmutableArray<Diagnostic> GeneratorDiagnostics { get; }

        /// <summary>Everything the compiler reported about the result, generated sources included.</summary>
        internal ImmutableArray<Diagnostic> CompilationDiagnostics => Compilation.GetDiagnostics();

        /// <summary>The generated file with the given hint name, or null.</summary>
        internal string? GeneratedSource(string hintNameSuffix)
        {
            foreach (SyntaxTree tree in Compilation.SyntaxTrees)
            {
                if (tree.FilePath.EndsWith(hintNameSuffix, StringComparison.Ordinal))
                {
                    return tree.ToString();
                }
            }

            return null;
        }
    }

    /// <summary>A ledger input file, as MSBuild would hand it over.</summary>
    internal sealed class LedgerFile
    {
        internal LedgerFile(string path, string kind, string text)
        {
            Path = path;
            Kind = kind;
            Text = text;
        }

        internal string Path { get; }

        /// <summary>The <c>DdLedger</c> metadata: <c>decision-set</c> or <c>ledger-export</c>.</summary>
        internal string Kind { get; }

        internal string Text { get; }
    }

    internal static Result Run(string consumerSource, IEnumerable<LedgerFile> files, string? archLayer = null, string assemblyName = "Consumer")
    {
        CSharpCompilation compilation = CSharpCompilation.Create(
            assemblyName,
            new[] { CSharpSyntaxTree.ParseText(consumerSource, new CSharpParseOptions(LanguageVersion.Latest)) },
            ReferenceAssemblies(),
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, nullableContextOptions: NullableContextOptions.Enable));

        List<AdditionalText> additional = new List<AdditionalText>();
        Dictionary<string, string> metadata = new Dictionary<string, string>(StringComparer.Ordinal);

        foreach (LedgerFile file in files)
        {
            additional.Add(new InMemoryAdditionalText(file.Path, file.Text));
            metadata[file.Path] = file.Kind;
        }

        OptionsProvider options = new OptionsProvider(metadata, archLayer);

        GeneratorDriver driver = CSharpGeneratorDriver.Create(
            generators: new[] { new DecisionLedgerGenerator().AsSourceGenerator() },
            additionalTexts: additional,
            parseOptions: new CSharpParseOptions(LanguageVersion.Latest),
            optionsProvider: options);

        driver = driver.RunGeneratorsAndUpdateCompilation(compilation, out Compilation updated, out ImmutableArray<Diagnostic> diagnostics);

        return new Result(updated, diagnostics);
    }

    /// <summary>
    /// Emits the compilation and reads the result back as metadata, which is the only way to test a
    /// claim about what a *referencing* project can see.
    /// </summary>
    internal static IAssemblySymbol EmitAndReadMetadata(Compilation compilation)
    {
        using MemoryStream stream = new MemoryStream();
        Microsoft.CodeAnalysis.Emit.EmitResult result = compilation.Emit(stream);

        if (!result.Success)
        {
            string errors = string.Join(
                Environment.NewLine,
                result.Diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error).Select(d => d.ToString()));
            throw new InvalidOperationException("The consumer did not compile:" + Environment.NewLine + errors);
        }

        stream.Position = 0;
        PortableExecutableReference reference = MetadataReference.CreateFromStream(stream);

        CSharpCompilation reader = CSharpCompilation.Create(
            "Reader",
            Array.Empty<SyntaxTree>(),
            ReferenceAssemblies().Concat(new[] { reference }),
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        return (IAssemblySymbol)reader.GetAssemblyOrModuleSymbol(reference)!;
    }

    /// <summary>
    /// The reference assemblies of the runtime the tests are running on. Enough to compile a
    /// consumer, and no package to pin for it.
    /// </summary>
    private static IEnumerable<MetadataReference> ReferenceAssemblies()
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

    private sealed class InMemoryAdditionalText : AdditionalText
    {
        private readonly SourceText text;

        internal InMemoryAdditionalText(string path, string text)
        {
            Path = path;
            this.text = SourceText.From(text);
        }

        public override string Path { get; }

        public override SourceText GetText(CancellationToken cancellationToken = default) => text;
    }

    private sealed class OptionsProvider : AnalyzerConfigOptionsProvider
    {
        private readonly Dictionary<string, string> ledgerKindByPath;

        internal OptionsProvider(Dictionary<string, string> ledgerKindByPath, string? archLayer)
        {
            this.ledgerKindByPath = ledgerKindByPath;
            GlobalOptions = new Options(archLayer is null
                ? new Dictionary<string, string>(StringComparer.Ordinal)
                : new Dictionary<string, string>(StringComparer.Ordinal) { ["build_property.ArchLayer"] = archLayer });
        }

        public override AnalyzerConfigOptions GlobalOptions { get; }

        public override AnalyzerConfigOptions GetOptions(SyntaxTree tree) =>
            new Options(new Dictionary<string, string>(StringComparer.Ordinal));

        public override AnalyzerConfigOptions GetOptions(AdditionalText textFile)
        {
            Dictionary<string, string> values = new Dictionary<string, string>(StringComparer.Ordinal);

            if (ledgerKindByPath.TryGetValue(textFile.Path, out string? kind))
            {
                values["build_metadata.AdditionalFiles.DdLedger"] = kind;
            }

            return new Options(values);
        }

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
