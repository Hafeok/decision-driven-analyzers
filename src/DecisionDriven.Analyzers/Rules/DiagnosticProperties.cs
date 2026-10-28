namespace DecisionDriven.Analyzers.Rules;

/// <summary>
/// The keys of the properties a rule attaches to a diagnostic for its code fix to read.
/// </summary>
/// <remarks>
/// Linked into the code-fixes assembly the way <see cref="DiagnosticIds"/> is, so that a fix reads
/// what the analyzer wrote by the same name and neither assembly binds to the other at run time.
/// </remarks>
internal static class DiagnosticProperties
{
    /// <summary>
    /// On a DD0022 mismatch: the name the namespace declaration should carry, when changing it is
    /// mechanical. Absent when it is not (a nested block whose expected name does not start with
    /// its containing namespace), and the fix is then not offered.
    /// </summary>
    internal const string NamespaceFolderNewName = "NewName";
}
