using Microsoft.CodeAnalysis;

namespace DecisionDriven.Analyzers.Rules;

/// <summary>
/// How a symbol is named in a diagnostic message.
/// </summary>
/// <remarks>
/// <c>DiagnosticMessages.MessageAsksTheDecisionQuestion</c> puts the finding first, and a finding
/// that names <c>IQuadSource</c> when there are two of them has not said what was found. Namespaces
/// are included and the global:: prefix is not, which is how a reader would write the name
/// themselves.
/// </remarks>
internal static class Display
{
    /// <summary>The format every contract rule names a type with.</summary>
    internal static readonly SymbolDisplayFormat Format = new SymbolDisplayFormat(
        globalNamespaceStyle: SymbolDisplayGlobalNamespaceStyle.Omitted,
        typeQualificationStyle: SymbolDisplayTypeQualificationStyle.NameAndContainingTypesAndNamespaces,
        genericsOptions: SymbolDisplayGenericsOptions.IncludeTypeParameters,
        miscellaneousOptions: SymbolDisplayMiscellaneousOptions.UseSpecialTypes);
}
