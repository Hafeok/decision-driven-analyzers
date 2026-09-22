using Xunit;

namespace DecisionDriven.Report.Tests;

/// <summary>
/// The tool can say which build of itself is running.
/// </summary>
/// <remarks>
/// Thin, but not pointless: the version comes from the versioning tool through an assembly
/// attribute, so this fails if versioning stops being wired up, and a tool that cannot say
/// what it is makes every report it produces unattributable.
/// </remarks>
public sealed class ToolVersionTest
{
    [Fact]
    public void The_tool_reports_a_version()
    {
        string version = ToolVersion.Version;

        Assert.False(string.IsNullOrWhiteSpace(version));

        // Source-control metadata belongs in the assembly attribute, not on the line a
        // person reads.
        Assert.DoesNotContain('+', version);
    }

    [Fact]
    public void The_tool_names_itself_alongside_its_version()
    {
        string described = ToolVersion.Describe();

        Assert.StartsWith("DecisionDriven.Report ", described);
        Assert.EndsWith(ToolVersion.Version, described);
    }
}
