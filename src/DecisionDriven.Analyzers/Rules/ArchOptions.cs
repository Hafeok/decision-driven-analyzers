using Microsoft.CodeAnalysis.Diagnostics;

namespace DecisionDriven.Analyzers.Rules;

/// <summary>
/// What a consumer said about the project being compiled.
/// </summary>
/// <remarks>
/// <para>
/// <c>TwoPackages.ConfigurationViaMsBuildProperties</c>: this is the whole of what the rules know
/// about a project, and it arrives through <c>CompilerVisibleProperty</c> rather than through
/// anything the analyzers ship. Adopting a rule never means changing analyzer code.
/// </para>
/// <para>
/// Read once per compilation, in a compilation-start action, because these run on every keystroke.
/// </para>
/// </remarks>
internal readonly struct ArchOptions
{
    private ArchOptions(string? family, int? layer, bool compositionRoot, string? contractTypeAssemblies)
    {
        Family = family;
        Layer = layer;
        IsCompositionRoot = compositionRoot;
        ContractTypeAssemblies = contractTypeAssemblies;
    }

    /// <summary>The family a layering rule applies within. Projects in different families are unrelated.</summary>
    internal string? Family { get; }

    /// <summary>
    /// The project's layer, or null when it has none. Null is a real state and is not layer 0: a
    /// project that never declared a layer has not opted into layering at all.
    /// </summary>
    internal int? Layer { get; }

    /// <summary>True for the one project allowed to know every layer at once.</summary>
    internal bool IsCompositionRoot { get; }

    /// <summary>Assemblies whose types may appear on a contract surface.</summary>
    internal string? ContractTypeAssemblies { get; }

    internal static ArchOptions Read(AnalyzerConfigOptions options)
    {
        string? family = Get(options, "build_property.ArchFamily");

        int? layer = null;
        if (Get(options, "build_property.ArchLayer") is { Length: > 0 } layerText
            && int.TryParse(layerText, System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out int parsed))
        {
            layer = parsed;
        }

        bool compositionRoot = string.Equals(Get(options, "build_property.ArchCompositionRoot"), "true", System.StringComparison.OrdinalIgnoreCase);

        return new ArchOptions(family, layer, compositionRoot, Get(options, "build_property.ArchContractTypeAssemblies"));
    }

    private static string? Get(AnalyzerConfigOptions options, string key) =>
        options.TryGetValue(key, out string? value) && value is { Length: > 0 } ? value : null;
}
