using System.Text;

namespace DecisionDriven.Analyzers.Ledger;

/// <summary>
/// Turning ledger ids into C# identifiers.
/// </summary>
/// <remarks>
/// A ledger set id is "lowercase alphanumerics, dashes, dots" and a ledger namespace is written the
/// same way, so neither is a C# identifier. Both become PascalCase: <c>build-time-dependencies</c>
/// is <c>BuildTimeDependencies</c>, which is the string a citation has to be because a citation is
/// <c>typeof(BuildTimeDependencies.SomeKey)</c>.
///
/// Decision keys are not transformed. Their syntax already makes them identifiers, and changing one
/// would break the citation it is supposed to carry.
/// </remarks>
internal static class Identifiers
{
    /// <summary>
    /// The PascalCase type name for a set id or namespace id.
    /// </summary>
    internal static string ToPascalCase(string id)
    {
        if (id.Length == 0)
        {
            return id;
        }

        StringBuilder builder = new StringBuilder(id.Length);
        bool startOfWord = true;

        foreach (char c in id)
        {
            if (c == '-' || c == '.' || c == '_' || c == ' ')
            {
                startOfWord = true;
                continue;
            }

            if (startOfWord)
            {
                builder.Append(char.ToUpperInvariant(c));
                startOfWord = false;
            }
            else
            {
                builder.Append(c);
            }
        }

        string name = builder.ToString();

        // A ledger id may legally start with a digit; a C# identifier may not.
        if (name.Length > 0 && name[0] >= '0' && name[0] <= '9')
        {
            name = "_" + name;
        }

        return name;
    }
}
