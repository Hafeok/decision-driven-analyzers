using System;

namespace DecisionDriven.Report;

/// <summary>
/// Entry point for the <c>decisiondriven-report</c> tool.
/// </summary>
/// <remarks>
/// The tool exists to answer the questions a single compilation cannot: the tier-3 rules
/// of <c>rule-tiers.ThreeTiers</c> are about the shape of a whole dependency graph, and an
/// analyzer only ever sees one project at a time. None of that is implemented yet. Until
/// it is, the tool reports its own version, which is enough to prove that it packs as a
/// tool, installs, and runs.
/// </remarks>
internal static class Program
{
    internal static int Main()
    {
        Console.Out.WriteLine(ToolVersion.Describe());
        return 0;
    }
}
