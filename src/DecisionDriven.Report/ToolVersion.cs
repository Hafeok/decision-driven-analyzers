using System.Reflection;

namespace DecisionDriven.Report;

/// <summary>
/// The tool's own version, as it reports it.
/// </summary>
internal static class ToolVersion
{
    /// <summary>
    /// Gets the informational version of the running assembly, without the source-control
    /// metadata the compiler appends after a <c>+</c>.
    /// </summary>
    /// <remarks>
    /// The informational version is the one that carries the prerelease label, so it is
    /// the one worth showing. Builds from the trunk are prerelease and builds from a
    /// <c>v*</c> tag are not, which makes this string the quickest way to tell which of
    /// the two somebody has installed.
    /// </remarks>
    internal static string Version
    {
        get
        {
            string? informational = typeof(ToolVersion).Assembly
                .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?
                .InformationalVersion;

            if (string.IsNullOrWhiteSpace(informational))
            {
                return typeof(ToolVersion).Assembly.GetName().Version?.ToString() ?? "0.0.0";
            }

            int plus = informational!.IndexOf('+');
            return plus < 0 ? informational : informational.Substring(0, plus);
        }
    }

    /// <summary>
    /// Gets the one line the tool prints when it is run with no arguments.
    /// </summary>
    internal static string Describe() => "DecisionDriven.Report " + Version;
}
