namespace DecisionDriven.Analyzers.Rules;

/// <summary>
/// The DD family of <c>RuleTiers.IdFamilies</c>: rules a consumer's own code can violate.
/// </summary>
/// <remarks>
/// Generic rule ids run from DD0001 (<c>TwoPackages.RuleIdPrefixDD</c>). An id is never reused,
/// even after a rule is removed, because a suppression or a doc link written against the old
/// meaning would silently come to mean something else.
/// </remarks>
internal static class DiagnosticIds
{
    /// <summary>A reference that does not point strictly downward through the layers.</summary>
    internal const string LayerReference = "DD0001";

    /// <summary>An <c>InternalsVisibleTo</c> grant to something that is not a test assembly.</summary>
    internal const string InternalsVisibleTo = "DD0002";

    /// <summary>A service resolved at runtime outside the composition root.</summary>
    internal const string ServiceLocation = "DD0003";
}
