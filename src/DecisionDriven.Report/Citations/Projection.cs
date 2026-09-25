using System;
using System.Collections.Generic;
using System.Linq;
using DecisionDriven.Analyzers.Ledger;
using DecisionDriven.Report.Metadata;

namespace DecisionDriven.Report.Citations;

/// <summary>A citation with its history attached.</summary>
/// <param name="Citation">The citing symbol.</param>
/// <param name="SourceFile">Its file, relative to the repository; null when the PDB did not say.</param>
/// <param name="Commit">The commit that introduced it; null when none could be found.</param>
/// <param name="CitesVersion">The cited decision's tip version at that commit; null when there was none.</param>
/// <param name="CurrentVersion">The cited decision's tip version now.</param>
/// <param name="NewSince">True when the introducing commit is not in the <c>--since</c> ref's history.</param>
internal sealed record Attributed(
    Citation Citation,
    string? SourceFile,
    string? Commit,
    string? CitesVersion,
    string? CurrentVersion,
    bool NewSince)
{
    /// <summary>
    /// Written against a version that is no longer the tip. The ledger's own invalidation, applied to
    /// code: the decision moved and the code that cited it did not.
    /// </summary>
    internal bool Stale => CitesVersion is not null && CurrentVersion is not null && CitesVersion != CurrentVersion;
}

/// <summary>
/// Every citation, dated by the commit that introduced it and tied to the version it cited.
/// </summary>
/// <remarks>
/// <c>DecisionsAsTypes.CitationVersionDerivedNotWritten</c>: nothing about a citation's version is
/// in source, because a hash in an attribute would restate a ledger fact and go stale on revision.
/// This is where it is derived instead - blame the citing symbol to its introducing commit, and
/// read the decision's tip as the ledger stood then.
/// </remarks>
internal sealed class Projection
{
    private Projection(IReadOnlyList<Attributed> citations, IReadOnlyList<Decision> uncited, string? since)
    {
        Citations = citations;
        Uncited = uncited;
        Since = since;
    }

    internal IReadOnlyList<Attributed> Citations { get; }

    /// <summary>Decisions in the current ledger that nothing cites: implicit somewhere, or dead. The report decides neither.</summary>
    internal IReadOnlyList<Decision> Uncited { get; }

    internal string? Since { get; }

    internal static Projection Build(
        IReadOnlyList<Citation> found,
        Git git,
        LedgerHistory ledger,
        string repositoryRoot,
        string ledgerDirectory,
        string? since)
    {
        List<Attributed> attributed = new List<Attributed>();

        foreach (Citation citation in found)
        {
            string? file = citation.SourceFile is { } document ? Sources.InRepository(document, repositoryRoot) : null;
            string? commit = git.IntroducingCommit(file, citation.Key, ledgerDirectory);

            string? cites = commit is null
                ? null
                : LedgerHistory.Find(ledger.At(commit), citation.LedgerNamespace, citation.DecisionId) is { } then
                    ? LedgerHistory.VersionOf(then)
                    : null;

            string? current = LedgerHistory.Find(ledger.Current, citation.LedgerNamespace, citation.DecisionId) is { } now
                ? LedgerHistory.VersionOf(now)
                : null;

            bool newSince = since is not null && commit is not null && !git.IsAncestor(commit, since);

            attributed.Add(new Attributed(citation, file, commit, cites, current, newSince));
        }

        HashSet<string> cited = new HashSet<string>(found.Select(c => c.DecisionId), StringComparer.Ordinal);

        List<Decision> uncited = ledger.Current.Values
            .SelectMany(ns => ns.Decisions.Values)
            .Where(decision => !cited.Contains(decision.Id) && decision.Tip is not null && !decision.IsRevokedWithoutSuccessor)
            .OrderBy(decision => decision.Id, StringComparer.Ordinal)
            .ToList();

        return new Projection(
            attributed
                .OrderBy(a => a.Citation.Symbol, StringComparer.Ordinal)
                .ThenBy(a => a.Citation.Attribute, StringComparer.Ordinal)
                .ThenBy(a => a.Citation.DecisionId, StringComparer.Ordinal)
                .ToList(),
            uncited,
            since);
    }
}
