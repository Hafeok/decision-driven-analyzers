using System;

namespace DecisionDriven.Analyzers.Tests.Ledger;

/// <summary>
/// The two ways of saying the same thing, so that tests can assert they say it identically.
/// </summary>
internal static class LedgerInput
{
    internal const string Namespace = "sample-ns";

    /// <summary>A set file in the interim front-matter form.</summary>
    internal static string FrontMatter(string setId, string key, string statement, string? acceptedBy = null, string? revokedAt = null)
    {
        string acceptance = acceptedBy is null
            ? string.Empty
            : "    accepted-by: " + acceptedBy + Environment.NewLine + "    accepted-at: 2026-01-01T00:00:00Z" + Environment.NewLine;

        string revocation = revokedAt is null
            ? string.Empty
            : "    revoked-at: " + revokedAt + Environment.NewLine;

        return "---" + Environment.NewLine
            + "set: " + setId + Environment.NewLine
            + "namespace: " + Namespace + Environment.NewLine
            + "decisions:" + Environment.NewLine
            + "  - key: " + key + Environment.NewLine
            + "    statement: \"" + statement + "\"" + Environment.NewLine
            + acceptance
            + revocation
            + "---" + Environment.NewLine
            + Environment.NewLine
            + "Prose the reader must ignore." + Environment.NewLine;
    }

    /// <summary>
    /// The same decision as N-Triples.
    /// </summary>
    /// <remarks>
    /// The decision and version ids are the ones the front-matter reader synthesises, because the
    /// generated source carries the decision id and the two forms are supposed to produce the same
    /// source. A real export carries a ULID and a content hash; what is being tested here is that
    /// the two readers agree, not that a ULID round-trips.
    /// </remarks>
    internal static string NTriples(string setId, string key, string statement, string? acceptedBy = null, string? revokedAt = null)
    {
        const string Ledger = "urn:ledger:ns#";
        const string Rdf = "http://www.w3.org/1999/02/22-rdf-syntax-ns#";

        string decision = "dec:" + Namespace + "/" + key;
        string version = "urn:interim:" + Namespace + "/" + key;

        string text =
            "<" + decision + "> <" + Rdf + "type> <" + Ledger + "Decision> ." + Environment.NewLine
            + "<" + decision + "> <" + Ledger + "namespace> \"" + Namespace + "\" ." + Environment.NewLine
            + "<" + version + "> <" + Rdf + "type> <" + Ledger + "DecisionVersion> ." + Environment.NewLine
            + "<" + version + "> <" + Ledger + "ofDecision> <" + decision + "> ." + Environment.NewLine
            + "<" + version + "> <" + Ledger + "set> \"" + setId + "\" ." + Environment.NewLine
            + "<" + version + "> <" + Ledger + "key> \"" + key + "\" ." + Environment.NewLine
            + "<" + version + "> <" + Ledger + "statement> \"" + statement + "\" ." + Environment.NewLine;

        if (acceptedBy is not null)
        {
            string acceptance = "urn:acceptance:" + key;
            text += "<" + acceptance + "> <" + Rdf + "type> <" + Ledger + "Acceptance> ." + Environment.NewLine
                + "<" + acceptance + "> <" + Ledger + "signsVersion> <" + version + "> ." + Environment.NewLine
                + "<" + acceptance + "> <" + Ledger + "acceptedBy> <" + acceptedBy + "> ." + Environment.NewLine
                + "<" + acceptance + "> <" + Ledger + "acceptedAt> \"2026-01-01T00:00:00Z\"^^<http://www.w3.org/2001/XMLSchema#dateTime> ." + Environment.NewLine;
        }

        if (revokedAt is not null)
        {
            text += "<" + decision + "> <" + Ledger + "revokedAt> \"" + revokedAt + "\" ." + Environment.NewLine;
        }

        return text;
    }

    internal static GeneratorHarness.LedgerFile AsSet(string text, string path = "decisions/set.md") =>
        new GeneratorHarness.LedgerFile(path, "decision-set", text);

    internal static GeneratorHarness.LedgerFile AsExport(string text, string path = "decisions/export.nt") =>
        new GeneratorHarness.LedgerFile(path, "ledger-export", text);
}
