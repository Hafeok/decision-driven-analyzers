using System;
using System.Collections.Generic;

namespace DecisionDriven.Analyzers.Ledger;

/// <summary>
/// The interim input: a markdown set file whose YAML front matter lists decisions.
/// </summary>
/// <remarks>
/// <para>
/// <c>DecisionsAsTypes.InterimFrontMatterUntilExport</c>. This exists because the ledger format does
/// not carry keys yet and decision-cli cannot export. It maps one-to-one onto the same model as the
/// N-Triples reader, so the generator has one model and two ways of filling it, and the day the
/// export arrives these files are deleted rather than reconciled.
/// </para>
/// <para>
/// Not a YAML parser. The front matter shape is fixed by the format the bundle documents, and this
/// reads exactly that shape: a <c>set</c>, a <c>namespace</c>, and a <c>decisions</c> list whose
/// entries carry <c>key</c>, <c>statement</c>, and optionally <c>accepted-by</c>, <c>accepted-at</c>
/// and <c>revoked-at</c>. Anything else on the line is ignored rather than guessed at.
/// </para>
/// </remarks>
internal static class FrontMatterReader
{
    /// <summary>
    /// The decision id a front-matter decision gets.
    /// </summary>
    /// <remarks>
    /// Real decisions are <c>dec:&lt;ns&gt;/&lt;ulid&gt;</c>. There is no ULID here, and inventing one
    /// per build would make the generated source change every compilation. The key is unique per
    /// namespace by the same decision that defines it, so it serves as the identity until import.
    /// </remarks>
    internal static string DecisionId(string ledgerNamespace, string key) => "dec:" + ledgerNamespace + "/" + key;

    /// <summary>The version id a front-matter decision's single version gets.</summary>
    internal static string VersionId(string ledgerNamespace, string key) => "urn:interim:" + ledgerNamespace + "/" + key;

    /// <summary>
    /// Folds one set file into the model. A file with no front matter contributes nothing, which is
    /// how the repository's own prose files under <c>docs/</c> pass through harmlessly.
    /// </summary>
    internal static void Read(string text, string sourcePath, Dictionary<string, LedgerNamespace> namespaces)
    {
        string[] lines = text.Split('\n');

        int start = -1;
        int end = -1;

        for (int i = 0; i < lines.Length; i++)
        {
            string line = lines[i].TrimEnd('\r');

            if (line != "---")
            {
                if (start < 0 && line.Trim().Length > 0)
                {
                    // Front matter has to be the first thing in the file.
                    return;
                }

                continue;
            }

            if (start < 0)
            {
                start = i;
            }
            else
            {
                end = i;
                break;
            }
        }

        if (start < 0 || end < 0)
        {
            return;
        }

        string? setId = null;
        string? ledgerNamespace = null;
        List<DecisionEntry> entries = new List<DecisionEntry>();
        DecisionEntry? current = null;

        for (int i = start + 1; i < end; i++)
        {
            string raw = lines[i].TrimEnd('\r');
            string line = raw.Trim();

            if (line.Length == 0 || line[0] == '#')
            {
                continue;
            }

            if (TryReadScalar(line, "set", out string? value))
            {
                setId = value;
                continue;
            }

            if (TryReadScalar(line, "namespace", out value))
            {
                ledgerNamespace = value;
                continue;
            }

            if (line.StartsWith("- ", StringComparison.Ordinal))
            {
                current = new DecisionEntry();
                entries.Add(current);
                line = line.Substring(2).Trim();
            }

            if (current is null)
            {
                continue;
            }

            if (TryReadScalar(line, "key", out value))
            {
                current.Key = value;
            }
            else if (TryReadScalar(line, "statement", out value))
            {
                current.Statement = value;
            }
            else if (TryReadScalar(line, "accepted-by", out value))
            {
                current.AcceptedBy = value;
            }
            else if (TryReadScalar(line, "accepted-at", out value))
            {
                current.AcceptedAt = value;
            }
            else if (TryReadScalar(line, "revoked-at", out value))
            {
                current.RevokedAt = value;
            }
        }

        if (setId is null || ledgerNamespace is null)
        {
            return;
        }

        if (!namespaces.TryGetValue(ledgerNamespace, out LedgerNamespace? ns))
        {
            ns = new LedgerNamespace(ledgerNamespace);
            namespaces.Add(ledgerNamespace, ns);
        }

        foreach (DecisionEntry entry in entries)
        {
            if (entry.Key is not { Length: > 0 } key)
            {
                continue;
            }

            string id = DecisionId(ledgerNamespace, key);

            if (ns.Decisions.TryGetValue(id, out Decision? existing) && existing.Versions.Count > 0)
            {
                // Another set file already claimed this key. Merging the two would silently make
                // them one decision, so the second claim is recorded and dropped instead.
                ns.DuplicateInterimKeys.Add(new InterimKeyClaim(key, existing.SourcePath ?? "another file", sourcePath));
                continue;
            }

            Decision decision = ns.GetOrAdd(id);
            decision.SourcePath ??= sourcePath;

            DecisionVersion version = new DecisionVersion(VersionId(ledgerNamespace, key), id)
            {
                SetId = setId,
                Key = key,
                Statement = entry.Statement,
            };

            decision.Versions.Add(version);

            if (entry.RevokedAt is { Length: > 0 })
            {
                decision.RevokedAt = entry.RevokedAt;
            }

            // An acceptance needs a signer. accepted-at without accepted-by is not an acceptance,
            // and the front-matter format says so: accepted-at is required *with* accepted-by.
            if (entry.AcceptedBy is { Length: > 0 })
            {
                decision.Acceptances.Add(new Acceptance(version.Id)
                {
                    By = entry.AcceptedBy,
                    At = entry.AcceptedAt,
                });
            }
        }
    }

    private static bool TryReadScalar(string line, string name, out string? value)
    {
        value = null;

        if (!line.StartsWith(name, StringComparison.Ordinal))
        {
            return false;
        }

        int after = name.Length;
        if (after >= line.Length || line[after] != ':')
        {
            return false;
        }

        string rest = line.Substring(after + 1).Trim();
        value = Unquote(rest);
        return true;
    }

    private static string Unquote(string s)
    {
        if (s.Length >= 2)
        {
            char first = s[0];
            char last = s[s.Length - 1];

            if ((first == '"' && last == '"') || (first == '\'' && last == '\''))
            {
                return s.Substring(1, s.Length - 2);
            }
        }

        // A trailing comment on a scalar line. Only stripped when it follows whitespace, so a '#'
        // inside an unquoted statement survives.
        int hash = s.IndexOf(" #", StringComparison.Ordinal);
        if (hash >= 0)
        {
            s = s.Substring(0, hash).TrimEnd();
        }

        return s;
    }

    private sealed class DecisionEntry
    {
        internal string? Key { get; set; }

        internal string? Statement { get; set; }

        internal string? AcceptedBy { get; set; }

        internal string? AcceptedAt { get; set; }

        internal string? RevokedAt { get; set; }
    }
}
