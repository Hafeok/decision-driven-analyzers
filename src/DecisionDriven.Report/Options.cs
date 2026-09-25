using System;
using System.Collections.Generic;

namespace DecisionDriven.Report;

/// <summary>
/// The command line.
/// </summary>
/// <remarks>
/// <code>
/// decisiondriven-report --assembly &lt;file-or-directory&gt;... [--repo &lt;dir&gt;] [--ledger &lt;dir&gt;]
///                       [--since &lt;ref&gt;] [--out &lt;dir&gt;]
/// decisiondriven-report --version
/// </code>
/// </remarks>
internal sealed class Options
{
    internal const string Usage =
        "usage: decisiondriven-report --assembly <file-or-directory> [--assembly ...] [--repo <dir>] "
        + "[--ledger <dir>] [--since <ref>] [--out <dir>]\n"
        + "       decisiondriven-report --version\n"
        + "\n"
        + "  --assembly  a built assembly, or a directory whose *.dll are read; repeat for more\n"
        + "  --repo      the repository root, for git history (default: the current directory)\n"
        + "  --ledger    the ledger directory, relative to the repository (default: docs/decisions)\n"
        + "  --since     a ref; decisions first cited after it are listed as newly cited\n"
        + "  --out       where report.md and citations.nt are written (default: artifacts/report)\n";

    internal List<string> Assemblies { get; } = new List<string>();

    internal string Repository { get; private set; } = ".";

    internal string Ledger { get; private set; } = "docs/decisions";

    internal string? Since { get; private set; }

    internal string Out { get; private set; } = "artifacts/report";

    internal bool Version { get; private set; }

    /// <summary>Parses the arguments, or returns the reason they could not be.</summary>
    internal static (Options? Options, string? Error) Parse(IReadOnlyList<string> args)
    {
        Options options = new Options();

        // No arguments at all is the version, which is what the tool printed before it did anything
        // else and what an install check runs.
        if (args.Count == 0)
        {
            options.Version = true;
            return (options, null);
        }

        for (int i = 0; i < args.Count; i++)
        {
            string arg = args[i];

            if (arg is "--version")
            {
                options.Version = true;
                continue;
            }

            if (arg is "-h" or "--help")
            {
                return (null, string.Empty);
            }

            if (i + 1 >= args.Count)
            {
                return (null, arg + " needs a value.");
            }

            string value = args[++i];

            switch (arg)
            {
                case "--assembly":
                    options.Assemblies.Add(value);
                    break;
                case "--repo":
                    options.Repository = value;
                    break;
                case "--ledger":
                    options.Ledger = value;
                    break;
                case "--since":
                    options.Since = value;
                    break;
                case "--out":
                    options.Out = value;
                    break;
                default:
                    return (null, "Unknown option " + arg + ".");
            }
        }

        if (!options.Version && options.Assemblies.Count == 0)
        {
            return (null, "Nothing to read: give at least one --assembly.");
        }

        return (options, null);
    }
}
