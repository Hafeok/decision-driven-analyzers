using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using DecisionDriven.Report.Citations;
using DecisionDriven.Report.Metadata;
using DecisionDriven.Report.Metrics;
using DecisionDriven.Report.Output;

namespace DecisionDriven.Report;

/// <summary>
/// Entry point for the <c>decisiondriven-report</c> tool.
/// </summary>
/// <remarks>
/// <para>
/// The tier-3 half of <c>RuleTiers.ThreeTiers</c>: the questions a single compilation cannot answer,
/// because they are about the shape of a whole dependency graph or the history of a repository, and
/// an analyzer sees one project at a time.
/// </para>
/// <para>
/// <c>WholeGraphReport.NothingGatesUntilBaselineDecision</c>: the exit code says whether the report
/// could be produced, never what it found. 0 is a report, 2 is a command line that could not be
/// understood, 1 is inputs that could not be read.
/// </para>
/// </remarks>
internal static class Program
{
    internal static int Main(string[] args)
    {
        (Options? options, string? error) = Options.Parse(args);

        if (options is null)
        {
            if (!string.IsNullOrEmpty(error))
            {
                Console.Error.WriteLine(error);
            }

            Console.Error.Write(Options.Usage);
            return string.IsNullOrEmpty(error) ? 0 : 2;
        }

        if (options.Version)
        {
            Console.Out.WriteLine(ToolVersion.Describe());
            return 0;
        }

        return Run(options, Console.Out, Console.Error);
    }

    /// <summary>Produces the report. Separate from <see cref="Main"/> so the tests can drive it.</summary>
    internal static int Run(Options options, TextWriter output, TextWriter errors)
    {
        List<LoadedAssembly> assemblies = new List<LoadedAssembly>();

        try
        {
            foreach (string path in Expand(options.Assemblies, errors))
            {
                if (LoadedAssembly.Open(path) is not { } assembly)
                {
                    continue;
                }

                // One copy of each assembly. A directory of build output carries copies of what it
                // references, and counting each twice would double every coupling it has.
                if (assemblies.Any(existing => existing.Name == assembly.Name))
                {
                    assembly.Dispose();
                    continue;
                }

                assemblies.Add(assembly);
            }

            if (assemblies.Count == 0)
            {
                errors.WriteLine("No assembly could be read from " + string.Join(", ", options.Assemblies) + ".");
                return 1;
            }

            string repository = Path.GetFullPath(options.Repository);
            Git git = new Git(repository);
            LedgerHistory ledger = new LedgerHistory(git, repository, options.Ledger);

            IReadOnlyList<PackageRow> packages = PackageMetrics.Compute(assemblies);
            IReadOnlyList<ContractRow> contracts = ContractUsage.Compute(assemblies);
            IReadOnlyList<CohesionRow> cohesion = Cohesion.Compute(assemblies);
            Projection projection = Projection.Build(
                CitationFinder.Find(assemblies), git, ledger, repository, options.Ledger, options.Since);

            string markdown = MarkdownReport.Write(ToolVersion.Version, git.Head, packages, contracts, cohesion, projection);
            string triples = CitationTriples.Write(projection, git.ObjectFormat);

            Directory.CreateDirectory(options.Out);
            File.WriteAllText(Path.Combine(options.Out, "report.md"), markdown);
            File.WriteAllText(Path.Combine(options.Out, "citations.nt"), triples);

            output.Write(markdown);
            return 0;
        }
        finally
        {
            foreach (LoadedAssembly assembly in assemblies)
            {
                assembly.Dispose();
            }
        }
    }

    private static IEnumerable<string> Expand(IEnumerable<string> inputs, TextWriter errors)
    {
        foreach (string input in inputs)
        {
            if (Directory.Exists(input))
            {
                foreach (string file in Directory.EnumerateFiles(input, "*.dll").OrderBy(f => f, StringComparer.Ordinal))
                {
                    yield return file;
                }
            }
            else if (File.Exists(input))
            {
                yield return input;
            }
            else
            {
                errors.WriteLine("Not found: " + input);
            }
        }
    }
}
