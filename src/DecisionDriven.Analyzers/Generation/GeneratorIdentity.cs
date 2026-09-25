using System;
using System.Reflection;
using Microsoft.CodeAnalysis;

namespace DecisionDriven.Analyzers.Generation;

/// <summary>
/// What a generator run leaves behind that a hand-written file cannot.
/// </summary>
/// <remarks>
/// <para>
/// <c>DecisionsAsTypes.AttributeArgumentsMustBeGenerated</c>. Neither a comment header nor a marker
/// attribute of our own is a provenance signal on its own: both are text, and anyone can type text.
/// What cannot be typed is a compiler input. Roslyn gives every generated tree a synthetic path -
/// the generator's assembly name, the generator type's full name, the hint name - and a file on disk
/// does not get one, so forging it means forging the compiler rather than forging a file.
/// </para>
/// <para>
/// The attribute is the second signal and is deliberately the BCL's own
/// <c>System.CodeDom.Compiler.GeneratedCodeAttribute</c> rather than anything this package defines.
/// Adding an attribute of ours would extend ADR-A07's attribute table, which is a decision; reusing
/// the one every generator already emits is not.
/// </para>
/// <para>
/// Both live here because the generator writes them and DD0007 reads them, and a rule that read a
/// different constant from the one the generator wrote would pass or fail for no reason a reader
/// could find.
/// </para>
/// </remarks>
internal static class GeneratorIdentity
{
    /// <summary>The <c>tool</c> argument of the emitted <c>GeneratedCode</c> attribute.</summary>
    internal const string ToolName = "DecisionDriven.Analyzers";

    /// <summary>The attribute the generator puts on every set and decision type.</summary>
    internal const string GeneratedCodeAttribute = "System.CodeDom.Compiler.GeneratedCodeAttribute";

    /// <summary>The package version, without the build metadata a git hash would put in it.</summary>
    internal static readonly string Version = ReadVersion();

    /// <summary>
    /// The last two directories of every tree this generator produces: its assembly name, then its
    /// type's full name.
    /// </summary>
    /// <remarks>
    /// Read from the generator type rather than written out, so a rename of the generator cannot
    /// leave the rule looking for a path nothing produces any more.
    /// </remarks>
    internal static readonly string TreePathPrefix =
        (typeof(DecisionLedgerGenerator).Assembly.GetName().Name ?? ToolName)
        + "/" + typeof(DecisionLedgerGenerator).FullName;

    /// <summary>
    /// True when <paramref name="filePath"/> has the shape Roslyn gives this generator's output:
    /// <c>[&lt;base&gt;/]&lt;assembly&gt;/&lt;generator type&gt;/&lt;hint&gt;</c>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The base is empty when a test drives the generator in memory and is the compiler's output
    /// directory in a real build, so the shape is matched from the end. Matching from the start -
    /// which is what this did first - passed every in-memory test and rejected every real citation
    /// in every consumer; the samples job, building against the packed nupkg, is what found it.
    /// </para>
    /// <para>
    /// The shape alone is not provenance: a consumer can create two directories with these names.
    /// <see cref="OutputDirectory"/> is what anchors it.
    /// </para>
    /// </remarks>
    internal static bool IsGeneratedTreePath(string? filePath)
    {
        if (filePath is not { Length: > 0 })
        {
            return false;
        }

        string path = Normalise(filePath);
        string marker = TreePathPrefix + "/";
        int at = path.LastIndexOf(marker, StringComparison.Ordinal);

        if (at < 0 || (at > 0 && path[at - 1] != '/'))
        {
            return false;
        }

        string hint = path.Substring(at + marker.Length);
        return hint.Length > 0 && hint.IndexOf('/') < 0;
    }

    /// <summary>
    /// The directory this generator's output was placed in for this compilation, or null when the
    /// generator did not run.
    /// </summary>
    /// <remarks>
    /// Read from where <c>DecisionDriven.ContractAttribute</c> is declared. The generator emits that
    /// type into every compilation at post-initialization, so wherever it is, that is where this
    /// run's output went. A hand-written file cannot move it: declaring a second
    /// <c>ContractAttribute</c> makes the name ambiguous, and an ambiguous name resolves to nothing.
    /// That is what turns "a path with the right shape" into "a tree from this generator run".
    /// </remarks>
    internal static string? OutputDirectory(Compilation compilation)
    {
        if (compilation.GetTypeByMetadataName(ContractAttribute) is not { } marker
            || marker.DeclaringSyntaxReferences.IsEmpty)
        {
            return null;
        }

        string path = marker.DeclaringSyntaxReferences[0].SyntaxTree.FilePath;

        return IsGeneratedTreePath(path) ? DirectoryOf(path) : null;
    }

    /// <summary>The directory part of a tree path, normalised.</summary>
    internal static string DirectoryOf(string filePath)
    {
        string path = Normalise(filePath);
        int slash = path.LastIndexOf('/');
        return slash < 0 ? string.Empty : path.Substring(0, slash);
    }

    private const string ContractAttribute = "DecisionDriven.ContractAttribute";

    private static string Normalise(string path) => path.Replace('\\', '/');

    private static string ReadVersion()
    {
        Assembly assembly = typeof(GeneratorIdentity).Assembly;

        if (assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
            is { Length: > 0 } informational)
        {
            // MinVer appends "+<sha>". Keeping it would rewrite every generated file on every
            // commit, for a fact the ledger already records better than a source comment can.
            int plus = informational.IndexOf('+');
            return plus < 0 ? informational : informational.Substring(0, plus);
        }

        return assembly.GetName().Version?.ToString() ?? "0.0.0";
    }
}
