using System;
using System.Collections.Generic;
using System.Security.Cryptography;
using System.Text;
using DecisionDriven.Report.Citations;

namespace DecisionDriven.Report.Output;

/// <summary>
/// The citation projection as N-Triples, for decision-cli to ingest as a read model.
/// </summary>
/// <remarks>
/// <para>
/// <c>WholeGraphReport.CitationProjectionAsLedgerEntities</c> and
/// <c>WholeGraphReport.LedgerCommitRequired</c>. One <c>ledger:Citation</c>, also a
/// <c>prov:Entity</c>, per citing symbol, with the commit that generated it as a
/// <c>ledger:Commit</c> and <c>prov:Activity</c> keyed <c>urn:git:sha1:</c> or
/// <c>urn:git:sha256:</c> by the repository's object format.
/// </para>
/// <para>
/// Three things here are this tool's assumptions until the ledger format settles them, and are
/// recorded in <c>docs/rules/ledger-input.md</c>: a citation's IRI is <c>urn:citation:</c> and a
/// SHA-256 of its symbol, attribute and decision, so it is stable across runs and the same citation
/// is the same node; <c>ledger:attribute</c> and <c>ledger:exceptionScope</c> are plain literals
/// holding the SKOS notation; and a citation that could not be dated has no
/// <c>ledger:citesVersion</c> or <c>prov:wasGeneratedBy</c> rather than a guessed one.
/// </para>
/// <para>
/// Lines are sorted, so two runs over the same repository produce the same file and a diff of it
/// is a diff of what changed.
/// </para>
/// </remarks>
internal static class CitationTriples
{
    private const string Rdf = "http://www.w3.org/1999/02/22-rdf-syntax-ns#";
    private const string Ledger = "urn:ledger:ns#";
    private const string Prov = "http://www.w3.org/ns/prov#";

    internal static string Write(Projection projection, string objectFormat)
    {
        SortedSet<string> lines = new SortedSet<string>(StringComparer.Ordinal);

        foreach (Attributed attributed in projection.Citations)
        {
            Citation citation = attributed.Citation;
            string subject = Iri(CitationIri(citation));

            lines.Add(subject + " " + Iri(Rdf + "type") + " " + Iri(Ledger + "Citation") + " .");
            lines.Add(subject + " " + Iri(Rdf + "type") + " " + Iri(Prov + "Entity") + " .");
            lines.Add(subject + " " + Iri(Ledger + "ofDecision") + " " + Iri(citation.DecisionId) + " .");
            lines.Add(subject + " " + Iri(Ledger + "symbol") + " " + Literal(citation.Symbol) + " .");
            lines.Add(subject + " " + Iri(Ledger + "attribute") + " " + Literal(citation.Attribute) + " .");

            if (citation.Scope is { } scope)
            {
                lines.Add(subject + " " + Iri(Ledger + "exceptionScope") + " " + Literal(scope) + " .");
            }

            if (attributed.CitesVersion is { } version)
            {
                lines.Add(subject + " " + Iri(Ledger + "citesVersion") + " " + Iri(version) + " .");
            }

            if (attributed.Commit is { } commit)
            {
                string activity = Iri("urn:git:" + objectFormat + ":" + commit);
                lines.Add(subject + " " + Iri(Prov + "wasGeneratedBy") + " " + activity + " .");
                lines.Add(activity + " " + Iri(Rdf + "type") + " " + Iri(Ledger + "Commit") + " .");
                lines.Add(activity + " " + Iri(Rdf + "type") + " " + Iri(Prov + "Activity") + " .");
            }
        }

        StringBuilder builder = new StringBuilder();
        foreach (string line in lines)
        {
            builder.Append(line).Append('\n');
        }

        return builder.ToString();
    }

    /// <summary>The citation's stable IRI.</summary>
    internal static string CitationIri(Citation citation)
    {
        byte[] hash = SHA256.HashData(Encoding.UTF8.GetBytes(
            citation.Symbol + "\n" + citation.Attribute + "\n" + citation.DecisionId));
        return "urn:citation:" + Convert.ToHexStringLower(hash);
    }

    private static string Iri(string value) => "<" + value + ">";

    /// <summary>An N-Triples string literal, escaped per the grammar.</summary>
    private static string Literal(string value)
    {
        StringBuilder builder = new StringBuilder(value.Length + 2).Append('"');

        foreach (char c in value)
        {
            switch (c)
            {
                case '\\':
                    builder.Append("\\\\");
                    break;
                case '"':
                    builder.Append("\\\"");
                    break;
                case '\n':
                    builder.Append("\\n");
                    break;
                case '\r':
                    builder.Append("\\r");
                    break;
                default:
                    builder.Append(c);
                    break;
            }
        }

        return builder.Append('"').ToString();
    }
}
