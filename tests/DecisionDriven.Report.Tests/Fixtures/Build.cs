using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Emit;

namespace DecisionDriven.Report.Tests.Fixtures;

/// <summary>
/// Compiles fixture assemblies to disk, each with a portable PDB beside it.
/// </summary>
internal static class Build
{
    /// <summary>
    /// The marker attributes as the generator emits them: internal, in namespace DecisionDriven, one
    /// copy per assembly. The report matches them by name, as the analyzers do.
    /// </summary>
    internal const string Markers = @"
namespace DecisionDriven
{
    [global::System.AttributeUsage(global::System.AttributeTargets.Assembly)]
    internal sealed class ArchLayerAttribute : global::System.Attribute
    {
        public ArchLayerAttribute(int layer) { }
    }

    [global::System.AttributeUsage(global::System.AttributeTargets.Interface | global::System.AttributeTargets.Class | global::System.AttributeTargets.Delegate)]
    internal sealed class ContractAttribute : global::System.Attribute
    {
        public ContractAttribute(global::System.Type decision) { }
        public string Role { get; set; } = string.Empty;
    }

    [global::System.AttributeUsage(global::System.AttributeTargets.Assembly, AllowMultiple = true)]
    internal sealed class DomainModelAttribute : global::System.Attribute
    {
        public DomainModelAttribute(string namespacePrefix, global::System.Type decision) { }
    }

    [global::System.AttributeUsage(global::System.AttributeTargets.All)]
    internal sealed class HotPathAttribute : global::System.Attribute
    {
        public HotPathAttribute(global::System.Type decision) { }
    }

    [global::System.AttributeUsage(global::System.AttributeTargets.All, AllowMultiple = true)]
    internal sealed class DesignDecisionAttribute : global::System.Attribute
    {
        public DesignDecisionAttribute(global::System.Type decision) { }
        public ExceptionScope Scope { get; set; }
    }

    internal enum ExceptionScope { Boundary, HotPath, Pool, Interop, Compatibility, Migration }
}
";

    /// <summary>A decision type as the generator emits one, with the constants the report reads.</summary>
    internal static string Decision(string ledgerNamespace, string setClass, string key) =>
        "namespace DecisionDriven.Ledger.Fixture { internal static class " + setClass + " { "
        + "public const string SetId = \"" + setClass.ToLowerInvariant() + "\"; "
        + "public static class " + key + " { "
        + "public const string Id = \"dec:" + ledgerNamespace + "/" + key + "\"; "
        + "public const string Key = \"" + key + "\"; "
        + "public const string Namespace = \"" + ledgerNamespace + "\"; } } }";

    /// <summary>
    /// Compiles <paramref name="files"/> - path to source - into <paramref name="directory"/>. The
    /// paths are what the PDB records as documents, so a test about source files passes real ones.
    /// </summary>
    internal static string Assembly(
        string directory,
        string name,
        IReadOnlyDictionary<string, string> files,
        IEnumerable<string>? references = null)
    {
        Directory.CreateDirectory(directory);

        List<SyntaxTree> trees = files
            .Select(file => CSharpSyntaxTree.ParseText(
                file.Value,
                new CSharpParseOptions(LanguageVersion.Latest),
                path: file.Key,
                encoding: System.Text.Encoding.UTF8))
            .ToList();

        List<MetadataReference> metadata = Platform().ToList();
        foreach (string reference in references ?? Array.Empty<string>())
        {
            metadata.Add(MetadataReference.CreateFromFile(reference));
        }

        CSharpCompilation compilation = CSharpCompilation.Create(
            name,
            trees,
            metadata,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, nullableContextOptions: NullableContextOptions.Enable));

        string dll = Path.Combine(directory, name + ".dll");
        string pdb = Path.Combine(directory, name + ".pdb");

        using (FileStream peStream = File.Create(dll))
        using (FileStream pdbStream = File.Create(pdb))
        {
            EmitResult result = compilation.Emit(
                peStream,
                pdbStream,
                options: new EmitOptions(debugInformationFormat: DebugInformationFormat.PortablePdb, pdbFilePath: pdb));

            if (!result.Success)
            {
                throw new InvalidOperationException(
                    "The fixture did not compile:" + Environment.NewLine
                    + string.Join(Environment.NewLine, result.Diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error)));
            }
        }

        return dll;
    }

    /// <summary>The same compilation, for comparing against what Roslyn says about its symbols.</summary>
    internal static CSharpCompilation Compilation(string name, IReadOnlyDictionary<string, string> files) =>
        CSharpCompilation.Create(
            name,
            files.Select(file => CSharpSyntaxTree.ParseText(file.Value, new CSharpParseOptions(LanguageVersion.Latest), path: file.Key)),
            Platform(),
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, nullableContextOptions: NullableContextOptions.Enable));

    private static IEnumerable<MetadataReference> Platform()
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
