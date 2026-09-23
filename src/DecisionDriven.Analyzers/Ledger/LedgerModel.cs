using System.Collections.Generic;

namespace DecisionDriven.Analyzers.Ledger;

/// <summary>
/// One version of a decision. Versions form a chain through <see cref="RevisionOf"/>; the end
/// of the chain is the tip, and the tip is what the generator emits.
/// </summary>
internal sealed class DecisionVersion
{
    internal DecisionVersion(string id, string decisionId)
    {
        Id = id;
        DecisionId = decisionId;
    }

    /// <summary>The version's own identity: a content hash in the ledger, synthetic for interim input.</summary>
    internal string Id { get; }

    /// <summary>The decision this is a version of.</summary>
    internal string DecisionId { get; }

    /// <summary>The set the decision belongs to at this version. Membership follows the tip.</summary>
    internal string? SetId { get; set; }

    /// <summary>
    /// The decision's key at this version. Immutable across versions of one decision, which is
    /// what makes a citation survive a revision.
    /// </summary>
    internal string? Key { get; set; }

    /// <summary>The decision this one supersedes, if any. Supersession is not obsolescence.</summary>
    internal string? Supersedes { get; set; }

    /// <summary>The version this one revises, if any.</summary>
    internal string? RevisionOf { get; set; }

    /// <summary>The one-line statement, carried for the interim form and unused by emission.</summary>
    internal string? Statement { get; set; }
}

/// <summary>
/// A signature over a version by an identity holding a role with <c>accept-decision</c>. An
/// acceptance that has been revoked no longer counts, which is why revocation is a field here
/// rather than a deletion.
/// </summary>
internal sealed class Acceptance
{
    internal Acceptance(string versionId)
    {
        VersionId = versionId;
    }

    /// <summary>The version signed.</summary>
    internal string VersionId { get; }

    /// <summary>The identity that signed it.</summary>
    internal string? By { get; set; }

    /// <summary>When it was signed.</summary>
    internal string? At { get; set; }

    /// <summary>Set when the acceptance itself has been revoked, which makes it stop counting.</summary>
    internal string? RevokedAt { get; set; }

    /// <summary>Whether this acceptance still counts towards the decision being accepted.</summary>
    internal bool IsLive => RevokedAt is null;
}

/// <summary>
/// A decision, with every version of it that the input carried.
/// </summary>
internal sealed class Decision
{
    internal Decision(string id, string ledgerNamespace)
    {
        Id = id;
        Namespace = ledgerNamespace;
    }

    /// <summary>The decision's identity, stable across versions and across supersession.</summary>
    internal string Id { get; }

    /// <summary>The ledger namespace this decision lives in.</summary>
    internal string Namespace { get; }

    /// <summary>Every version of this decision, in input order.</summary>
    internal List<DecisionVersion> Versions { get; } = new List<DecisionVersion>();

    /// <summary>Every acceptance of any version of this decision.</summary>
    internal List<Acceptance> Acceptances { get; } = new List<Acceptance>();

    /// <summary>Set when the decision itself was revoked.</summary>
    internal string? RevokedAt { get; set; }

    /// <summary>The interim set file this decision was read from, for the duplicate-key message.</summary>
    internal string? SourcePath { get; set; }

    /// <summary>Set when another decision supersedes this one; a successor keeps the code compiling.</summary>
    internal bool HasSuccessor { get; set; }

    /// <summary>
    /// The end of the revision chain: the version no other version revises. A chain with a cycle
    /// or a missing link falls back to the last version read, so that malformed input still emits
    /// something and the diagnostic, rather than a crash, is what the author sees.
    /// </summary>
    internal DecisionVersion? Tip
    {
        get
        {
            if (Versions.Count == 0)
            {
                return null;
            }

            HashSet<string> revised = new HashSet<string>();
            foreach (DecisionVersion version in Versions)
            {
                if (version.RevisionOf is { Length: > 0 } previous)
                {
                    revised.Add(previous);
                }
            }

            foreach (DecisionVersion version in Versions)
            {
                if (!revised.Contains(version.Id))
                {
                    return version;
                }
            }

            return Versions[Versions.Count - 1];
        }
    }

    /// <summary>
    /// Whether the tip version carries an acceptance that has not been revoked. This is the whole
    /// of what <c>DecisionsAsTypes.UnacceptedEmitsWarningObsolete</c> turns on.
    /// </summary>
    internal bool TipIsAccepted
    {
        get
        {
            DecisionVersion? tip = Tip;
            if (tip is null)
            {
                return false;
            }

            foreach (Acceptance acceptance in Acceptances)
            {
                if (acceptance.IsLive && acceptance.VersionId == tip.Id)
                {
                    return true;
                }
            }

            return false;
        }
    }

    /// <summary>
    /// Revoked with nobody to carry the key forward. <c>DecisionsAsTypes.RevokedEmitsErrorObsolete</c>:
    /// citing this cannot compile. A revoked decision that has a successor is a supersession, and
    /// supersession is not obsolescence.
    /// </summary>
    internal bool IsRevokedWithoutSuccessor => RevokedAt is { Length: > 0 } && !HasSuccessor;
}

/// <summary>
/// One key claimed by two interim set files.
/// </summary>
internal sealed class InterimKeyClaim
{
    internal InterimKeyClaim(string key, string firstPath, string secondPath)
    {
        Key = key;
        FirstPath = firstPath;
        SecondPath = secondPath;
    }

    internal string Key { get; }

    internal string FirstPath { get; }

    internal string SecondPath { get; }
}

/// <summary>
/// Everything one ledger namespace contains, which is what one generated C# namespace is built from.
/// </summary>
internal sealed class LedgerNamespace
{
    internal LedgerNamespace(string name)
    {
        Name = name;
    }

    /// <summary>The ledger namespace id, as written in the input.</summary>
    internal string Name { get; }

    /// <summary>Decisions by id.</summary>
    internal Dictionary<string, Decision> Decisions { get; } = new Dictionary<string, Decision>();

    /// <summary>
    /// Keys that more than one interim set file claimed, with the files that claimed them.
    /// </summary>
    /// <remarks>
    /// In the interim form the key is the identity: a front-matter decision has no ULID, so its id
    /// is derived from its key. Two files claiming one key therefore look exactly like one decision
    /// with two versions, and would be merged rather than reported. The reader records the
    /// collision here instead, so the duplicate is caught in the form where it is easiest to make.
    /// </remarks>
    internal List<InterimKeyClaim> DuplicateInterimKeys { get; } = new List<InterimKeyClaim>();

    internal Decision GetOrAdd(string id)
    {
        if (!Decisions.TryGetValue(id, out Decision? decision))
        {
            decision = new Decision(id, Name);
            Decisions.Add(id, decision);
        }

        return decision;
    }
}
