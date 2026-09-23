using System;
using System.Collections.Generic;
using System.Text;

namespace DecisionDriven.Analyzers.Ledger;

/// <summary>
/// One statement out of an N-Triples export.
/// </summary>
internal readonly struct Triple
{
    internal Triple(string subject, string predicate, string @object, bool objectIsLiteral)
    {
        Subject = subject;
        Predicate = predicate;
        Object = @object;
        ObjectIsLiteral = objectIsLiteral;
    }

    internal string Subject { get; }

    internal string Predicate { get; }

    internal string Object { get; }

    internal bool ObjectIsLiteral { get; }
}

/// <summary>
/// A reader for the ledger's N-Triples export.
/// </summary>
/// <remarks>
/// <para>
/// <c>DecisionsAsTypes.NTriplesExportIsGeneratorInput</c>: no Turtle parser and no dependencies.
/// N-Triples is one statement per line, each an absolute IRI or literal, which is why the ledger
/// exports it and why this file is a few hundred lines rather than a library.
/// </para>
/// <para>
/// This reads the subset the generator needs and is deliberately not a conformant N-Triples
/// processor: it does not resolve language tags or datatypes beyond stripping them, and it treats
/// anything it cannot split into three terms as an error rather than guessing.
/// </para>
/// </remarks>
internal static class NTriplesReader
{
    /// <summary>The ledger vocabulary.</summary>
    internal const string Ledger = "urn:ledger:ns#";

    /// <summary>The PROV vocabulary, used for the revision chain.</summary>
    internal const string Prov = "http://www.w3.org/ns/prov#";

    /// <summary>RDF, used for the type statements that say what a node is.</summary>
    internal const string Rdf = "http://www.w3.org/1999/02/22-rdf-syntax-ns#";

    /// <summary>
    /// Parses one line. Returns false for a blank line or a comment, which are not errors; sets
    /// <paramref name="malformed"/> for a line that should have been a statement and was not.
    /// </summary>
    internal static bool TryParseLine(string line, out Triple triple, out bool malformed)
    {
        triple = default;
        malformed = false;

        string trimmed = line.Trim();
        if (trimmed.Length == 0 || trimmed[0] == '#')
        {
            return false;
        }

        int position = 0;

        if (!TryReadIri(trimmed, ref position, out string? subject))
        {
            malformed = true;
            return false;
        }

        if (!TryReadIri(trimmed, ref position, out string? predicate))
        {
            malformed = true;
            return false;
        }

        SkipWhitespace(trimmed, ref position);
        if (position >= trimmed.Length)
        {
            malformed = true;
            return false;
        }

        string value;
        bool isLiteral;

        if (trimmed[position] == '"')
        {
            if (!TryReadLiteral(trimmed, ref position, out string? literal))
            {
                malformed = true;
                return false;
            }

            value = literal!;
            isLiteral = true;
        }
        else if (trimmed[position] == '<')
        {
            if (!TryReadIri(trimmed, ref position, out string? iri))
            {
                malformed = true;
                return false;
            }

            value = iri!;
            isLiteral = false;
        }
        else
        {
            // Blank nodes and anything else: not something this generator has a use for, and not
            // something it should silently drop.
            malformed = true;
            return false;
        }

        // What remains must be the statement terminator and nothing of substance.
        SkipWhitespace(trimmed, ref position);
        if (position >= trimmed.Length || trimmed[position] != '.')
        {
            malformed = true;
            return false;
        }

        triple = new Triple(subject!, predicate!, value, isLiteral);
        return true;
    }

    private static void SkipWhitespace(string s, ref int position)
    {
        while (position < s.Length && (s[position] == ' ' || s[position] == '\t'))
        {
            position++;
        }
    }

    private static bool TryReadIri(string s, ref int position, out string? iri)
    {
        iri = null;
        SkipWhitespace(s, ref position);

        if (position >= s.Length || s[position] != '<')
        {
            return false;
        }

        int end = s.IndexOf('>', position + 1);
        if (end < 0)
        {
            return false;
        }

        iri = s.Substring(position + 1, end - position - 1);
        position = end + 1;
        return true;
    }

    private static bool TryReadLiteral(string s, ref int position, out string? literal)
    {
        literal = null;

        if (position >= s.Length || s[position] != '"')
        {
            return false;
        }

        StringBuilder builder = new StringBuilder();
        int i = position + 1;
        bool closed = false;

        while (i < s.Length)
        {
            char c = s[i];

            if (c == '\\' && i + 1 < s.Length)
            {
                char escaped = s[i + 1];
                builder.Append(escaped switch
                {
                    'n' => '\n',
                    'r' => '\r',
                    't' => '\t',
                    '"' => '"',
                    '\\' => '\\',
                    _ => escaped,
                });
                i += 2;
                continue;
            }

            if (c == '"')
            {
                closed = true;
                i++;
                break;
            }

            builder.Append(c);
            i++;
        }

        if (!closed)
        {
            return false;
        }

        // A language tag or datatype follows the closing quote. The generator has no use for
        // either, so they are skipped rather than modelled.
        if (i < s.Length && s[i] == '@')
        {
            while (i < s.Length && s[i] != ' ' && s[i] != '\t' && s[i] != '.')
            {
                i++;
            }
        }
        else if (i + 1 < s.Length && s[i] == '^' && s[i + 1] == '^')
        {
            i += 2;
            if (i < s.Length && s[i] == '<')
            {
                int end = s.IndexOf('>', i);
                if (end < 0)
                {
                    return false;
                }

                i = end + 1;
            }
        }

        position = i;
        literal = builder.ToString();
        return true;
    }

    /// <summary>
    /// Folds an export into the model.
    /// </summary>
    /// <param name="text">The whole file.</param>
    /// <param name="fileName">Used only to say where a bad line is.</param>
    /// <param name="namespaces">Accumulator, so several files can build one model.</param>
    /// <param name="malformedLines">One-based line numbers that were not statements.</param>
    internal static void Read(
        string text,
        string fileName,
        Dictionary<string, LedgerNamespace> namespaces,
        List<int> malformedLines)
    {
        string[] lines = text.Split('\n');

        // Subject -> what the type statement said it is, so that acceptance nodes can be told from
        // version nodes without depending on the order statements arrive in.
        Dictionary<string, List<Triple>> bySubject = new Dictionary<string, List<Triple>>();
        Dictionary<string, string> nodeTypes = new Dictionary<string, string>();

        for (int i = 0; i < lines.Length; i++)
        {
            string line = lines[i].TrimEnd('\r');

            if (!TryParseLine(line, out Triple triple, out bool malformed))
            {
                if (malformed)
                {
                    malformedLines.Add(i + 1);
                }

                continue;
            }

            if (triple.Predicate == Rdf + "type" && !triple.ObjectIsLiteral)
            {
                nodeTypes[triple.Subject] = triple.Object;
            }

            if (!bySubject.TryGetValue(triple.Subject, out List<Triple>? statements))
            {
                statements = new List<Triple>();
                bySubject.Add(triple.Subject, statements);
            }

            statements.Add(triple);
        }

        // Decisions first: a version needs the decision it belongs to, and a decision needs its
        // namespace, before either can be placed.
        foreach (KeyValuePair<string, List<Triple>> node in bySubject)
        {
            if (!nodeTypes.TryGetValue(node.Key, out string? type) || type != Ledger + "Decision")
            {
                continue;
            }

            string ledgerNamespace = ReadNamespace(node.Value, node.Key);
            LedgerNamespace ns = GetOrAddNamespace(namespaces, ledgerNamespace);
            ns.GetOrAdd(node.Key);

            // ledger:id, prov:wasAttributedTo, prov:generatedAtTime and prov:wasGeneratedBy are on
            // this node too. The generator has no use for provenance: what it emits is a type per
            // decision, and who filed it when is the report tool's question.
            //
            // There is deliberately no decision-level revocation read here. The ledger has no
            // decision-level retirement, so RevokedEmitsErrorObsolete is fed only by the interim
            // front matter until the format grows one. docs/rules/ledger-input.md records it.
        }

        // Versions, which also tell us who supersedes whom.
        foreach (KeyValuePair<string, List<Triple>> node in bySubject)
        {
            if (!nodeTypes.TryGetValue(node.Key, out string? type) || type != Ledger + "DecisionVersion")
            {
                continue;
            }

            string? decisionId = null;
            foreach (Triple triple in node.Value)
            {
                if (triple.Predicate == Ledger + "ofDecision")
                {
                    decisionId = triple.Object;
                }
            }

            if (decisionId is null)
            {
                continue;
            }

            Decision? decision = Find(namespaces, decisionId);
            if (decision is null)
            {
                // A version of a decision the export did not declare. Place it under the namespace
                // its decision id carries, so it is still emitted rather than silently dropped.
                LedgerNamespace ns = GetOrAddNamespace(namespaces, NamespaceFromDecisionId(decisionId));
                decision = ns.GetOrAdd(decisionId);
            }

            DecisionVersion version = new DecisionVersion(node.Key, decisionId);

            foreach (Triple triple in node.Value)
            {
                if (triple.Predicate == Ledger + "set")
                {
                    version.SetId = triple.Object;
                }
                else if (triple.Predicate == Ledger + "key")
                {
                    version.Key = triple.Object;
                }
                else if (triple.Predicate == Ledger + "statement")
                {
                    version.Statement = triple.Object;
                }
                else if (triple.Predicate == Ledger + "supersedes")
                {
                    version.Supersedes = triple.Object;
                }
                else if (triple.Predicate == Prov + "wasRevisionOf")
                {
                    version.RevisionOf = triple.Object;
                }
            }

            decision.Versions.Add(version);
        }

        // Acceptances last: they point at versions, which now exist.
        foreach (KeyValuePair<string, List<Triple>> node in bySubject)
        {
            if (!nodeTypes.TryGetValue(node.Key, out string? type) || type != Ledger + "Acceptance")
            {
                continue;
            }

            string? versionId = null;
            foreach (Triple triple in node.Value)
            {
                if (triple.Predicate == Ledger + "signsVersion")
                {
                    versionId = triple.Object;
                }
            }

            if (versionId is null)
            {
                continue;
            }

            Acceptance acceptance = new Acceptance(versionId);
            foreach (Triple triple in node.Value)
            {
                if (triple.Predicate == Ledger + "ofDecision")
                {
                    acceptance.DecisionId = triple.Object;
                }
                else if (triple.Predicate == Ledger + "scope")
                {
                    acceptance.Scope = triple.Object;
                }
                else if (triple.Predicate == Prov + "wasAttributedTo")
                {
                    acceptance.AttributedTo = triple.Object;
                }
                else if (triple.Predicate == Prov + "generatedAtTime")
                {
                    acceptance.GeneratedAtTime = triple.Object;
                }
                else if (triple.Predicate == Ledger + "revokedAt")
                {
                    acceptance.RevokedAt = triple.Object;
                }
                else if (triple.Predicate == Ledger + "revokedBy")
                {
                    acceptance.RevokedBy = triple.Object;
                }
                else if (triple.Predicate == Ledger + "revocationReason")
                {
                    acceptance.RevocationReason = triple.Object;
                }
            }

            Decision? owner = acceptance.DecisionId is { Length: > 0 } ofDecision
                ? Find(namespaces, ofDecision)
                : null;

            owner ??= FindByVersion(namespaces, versionId);
            owner?.Acceptances.Add(acceptance);
        }

        MarkSuccessors(namespaces);
    }

    private static string ReadNamespace(List<Triple> statements, string decisionId)
    {
        foreach (Triple triple in statements)
        {
            if (triple.Predicate == Ledger + "namespace")
            {
                return triple.Object;
            }
        }

        return NamespaceFromDecisionId(decisionId);
    }

    /// <summary>
    /// A decision id is <c>dec:&lt;ns&gt;/&lt;ulid&gt;</c>, so the namespace can be recovered from it
    /// when the export did not state it separately.
    /// </summary>
    private static string NamespaceFromDecisionId(string decisionId)
    {
        int colon = decisionId.IndexOf(':');
        int slash = decisionId.IndexOf('/', colon < 0 ? 0 : colon + 1);

        if (colon >= 0 && slash > colon)
        {
            return decisionId.Substring(colon + 1, slash - colon - 1);
        }

        return "unknown";
    }

    private static LedgerNamespace GetOrAddNamespace(Dictionary<string, LedgerNamespace> namespaces, string name)
    {
        if (!namespaces.TryGetValue(name, out LedgerNamespace? ns))
        {
            ns = new LedgerNamespace(name);
            namespaces.Add(name, ns);
        }

        return ns;
    }

    private static Decision? Find(Dictionary<string, LedgerNamespace> namespaces, string decisionId)
    {
        foreach (LedgerNamespace ns in namespaces.Values)
        {
            if (ns.Decisions.TryGetValue(decisionId, out Decision? decision))
            {
                return decision;
            }
        }

        return null;
    }

    private static Decision? FindByVersion(Dictionary<string, LedgerNamespace> namespaces, string versionId)
    {
        foreach (LedgerNamespace ns in namespaces.Values)
        {
            foreach (Decision decision in ns.Decisions.Values)
            {
                foreach (DecisionVersion version in decision.Versions)
                {
                    if (version.Id == versionId)
                    {
                        return decision;
                    }
                }
            }
        }

        return null;
    }

    /// <summary>
    /// A decision that something supersedes has a successor, and a revoked decision with a successor
    /// is not obsolete: the key carries forward.
    /// </summary>
    private static void MarkSuccessors(Dictionary<string, LedgerNamespace> namespaces)
    {
        foreach (LedgerNamespace ns in namespaces.Values)
        {
            foreach (Decision decision in ns.Decisions.Values)
            {
                foreach (DecisionVersion version in decision.Versions)
                {
                    if (version.Supersedes is { Length: > 0 } superseded)
                    {
                        Decision? predecessor = Find(namespaces, superseded);
                        if (predecessor is not null)
                        {
                            predecessor.HasSuccessor = true;
                        }
                    }
                }
            }
        }
    }
}
