using System;
using System.Reflection;

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
    /// What Roslyn puts in front of the hint name in a generated tree's path.
    /// </summary>
    /// <remarks>
    /// Read from the generator type rather than written out, so a rename of the generator cannot
    /// leave the rule looking for a path nothing produces any more.
    /// </remarks>
    internal static readonly string TreePathPrefix =
        (typeof(DecisionLedgerGenerator).Assembly.GetName().Name ?? ToolName)
        + "/" + typeof(DecisionLedgerGenerator).FullName;

    /// <summary>
    /// True when <paramref name="filePath"/> is a path Roslyn synthesised for this generator's
    /// output.
    /// </summary>
    /// <remarks>
    /// The separator is the platform's, so both are normalised before comparing: a consumer
    /// building on Windows and one building on Linux are citing the same decisions.
    /// </remarks>
    internal static bool IsGeneratedTreePath(string? filePath) =>
        filePath is { Length: > 0 }
        && Normalise(filePath).StartsWith(TreePathPrefix + "/", StringComparison.Ordinal);

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
