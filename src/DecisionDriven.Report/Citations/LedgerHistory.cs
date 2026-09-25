using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using DecisionDriven.Analyzers.Ledger;

namespace DecisionDriven.Report.Citations;

/// <summary>
/// The ledger as it is now, and as it was at any commit.
/// </summary>
/// <remarks>
/// <para>
/// Read with the generator's own readers, linked into this tool, so that the report and the
/// generator are two read models of one ledger and cannot disagree about what it says.
/// </para>
/// <para>
/// <c>WholeGraphReport.CitationProjectionAsLedgerEntities</c> needs the version a citation was
/// written against: the cited decision's tip at the citation's introducing commit. A ledger export
/// names versions by content hash, and that hash is the answer. The interim front matter has no
/// version hash - its reader synthesises one fixed id per decision - so for those decisions the
/// version is a SHA-256 over the decision's own entry: namespace, set, key and statement. That is
/// an assumption of this tool, recorded in <c>docs/rules/ledger-input.md</c>, and it is what makes
/// "cited a version that is no longer the tip" mean something before the export exists: the
/// statement changed after the code cited it.
/// </para>
/// </remarks>
internal sealed class LedgerHistory
{
    private const string InterimVersionPrefix = "urn:interim:";

    private readonly Git git;
    private readonly string ledgerDirectory;
    private readonly Dictionary<string, Dictionary<string, LedgerNamespace>> byCommit =
        new Dictionary<string, Dictionary<string, LedgerNamespace>>(StringComparer.Ordinal);

    /// <param name="git">The repository the ledger lives in.</param>
    /// <param name="repositoryRoot">Its root on disk, for reading the ledger as it is now.</param>
    /// <param name="ledgerDirectory">The ledger's directory, relative to the repository root.</param>
    internal LedgerHistory(Git git, string repositoryRoot, string ledgerDirectory)
    {
        this.git = git;
        this.ledgerDirectory = ledgerDirectory.Replace('\\', '/').TrimEnd('/');
        Current = ReadDirectory(Path.Combine(repositoryRoot, ledgerDirectory));
    }

    /// <summary>The ledger in the working tree: what the build that produced the assemblies read.</summary>
    internal Dictionary<string, LedgerNamespace> Current { get; }

    /// <summary>The ledger at <paramref name="commit"/>, read from the commit rather than the disk.</summary>
    internal Dictionary<string, LedgerNamespace> At(string commit)
    {
        if (byCommit.TryGetValue(commit, out Dictionary<string, LedgerNamespace>? cached))
        {
            return cached;
        }

        Dictionary<string, LedgerNamespace> ledger = new Dictionary<string, LedgerNamespace>(StringComparer.Ordinal);

        foreach (string path in git.FilesAt(commit, ledgerDirectory))
        {
            if (git.Show(commit, path) is { } text)
            {
                Read(path, text, ledger);
            }
        }

        byCommit[commit] = ledger;
        return ledger;
    }

    /// <summary>A decision by namespace and id, or null when that ledger does not have it.</summary>
    internal static Decision? Find(Dictionary<string, LedgerNamespace> ledger, string ledgerNamespace, string decisionId) =>
        ledger.TryGetValue(ledgerNamespace, out LedgerNamespace? ns) && ns.Decisions.TryGetValue(decisionId, out Decision? decision)
            ? decision
            : null;

    /// <summary>
    /// The decision's tip version as a <c>urn:sha256:</c> IRI, or null when it has no tip.
    /// </summary>
    internal static string? VersionOf(Decision decision)
    {
        if (decision.Tip is not { } tip)
        {
            return null;
        }

        if (!tip.Id.StartsWith(InterimVersionPrefix, StringComparison.Ordinal))
        {
            return tip.Id;
        }

        string entry = string.Join(
            "\n",
            decision.Namespace,
            tip.SetId ?? string.Empty,
            tip.Key ?? string.Empty,
            tip.Statement ?? string.Empty);

        return "urn:sha256:" + Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(entry)));
    }

    private static Dictionary<string, LedgerNamespace> ReadDirectory(string directory)
    {
        Dictionary<string, LedgerNamespace> ledger = new Dictionary<string, LedgerNamespace>(StringComparer.Ordinal);

        if (!Directory.Exists(directory))
        {
            return ledger;
        }

        foreach (string path in Directory.EnumerateFiles(directory))
        {
            Read(path, File.ReadAllText(path), ledger);
        }

        return ledger;
    }

    /// <summary>Both forms, told apart by extension here: the report reads a directory, not MSBuild items.</summary>
    private static void Read(string path, string text, Dictionary<string, LedgerNamespace> ledger)
    {
        if (path.EndsWith(".md", StringComparison.OrdinalIgnoreCase))
        {
            FrontMatterReader.Read(text, Path.GetFileName(path), ledger);
        }
        else if (path.EndsWith(".nt", StringComparison.OrdinalIgnoreCase))
        {
            NTriplesReader.Read(text, Path.GetFileName(path), ledger, new List<int>());
        }
    }
}
