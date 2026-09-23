namespace DecisionDriven.Analyzers.Ledger;

/// <summary>
/// The key syntax from <c>DecisionsAsTypes.VersionLevelKeyCarriedAcrossSupersession</c>:
/// <c>^[A-Z][A-Za-z0-9]{0,63}$</c>.
/// </summary>
/// <remarks>
/// Checked by hand rather than with <see cref="System.Text.RegularExpressions.Regex"/>. This runs
/// once per decision on every keystroke in every consuming project, and the pattern is four
/// conditions; a compiled regex would cost more to start than this costs to run.
/// </remarks>
internal static class DecisionKey
{
    /// <summary>The pattern, as written in the decision, for use in diagnostic messages.</summary>
    internal const string Pattern = "^[A-Z][A-Za-z0-9]{0,63}$";

    internal static bool IsValid(string? key)
    {
        if (key is null || key.Length == 0 || key.Length > 64)
        {
            return false;
        }

        char first = key[0];
        if (first < 'A' || first > 'Z')
        {
            return false;
        }

        for (int i = 1; i < key.Length; i++)
        {
            char c = key[i];
            bool alphanumeric = (c >= 'A' && c <= 'Z') || (c >= 'a' && c <= 'z') || (c >= '0' && c <= '9');
            if (!alphanumeric)
            {
                return false;
            }
        }

        return true;
    }
}
