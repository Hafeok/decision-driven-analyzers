# Ledger input: what the generator reads

The decision ledger is the source; the generator is a read model over its export
(`DecisionsAsTypes.LedgerIsSourceGeneratorIsReadModel`). This page records exactly what the
generator reads, because the ledger format document is not in this repository yet and until it is,
what follows is the reader's **documented assumption** rather than a citation of a spec.

Both inputs arrive as `AdditionalFiles` carrying `DdLedger` metadata — `ledger-export` for the
N-Triples, `decision-set` for the interim markdown. The metadata is what tells them apart, not the
extension, so a consumer's unrelated markdown cannot become a decision set by having a `set:` line.

## Vocabularies

| Prefix | IRI |
| --- | --- |
| `ledger:` | `urn:ledger:ns#` |
| `prov:` | `http://www.w3.org/ns/prov#` |
| `rdf:` | `http://www.w3.org/1999/02/22-rdf-syntax-ns#` |

## Decision node — `rdf:type ledger:Decision`

| Predicate | Read | Used for |
| --- | --- | --- |
| `ledger:id` | no | The node IRI is the identity the generator uses. |
| `ledger:namespace` | **yes** | Which generated C# namespace the decision lands in. Falls back to the `<ns>` in `dec:<ns>/<ulid>`. |
| `prov:wasAttributedTo` | no | Provenance. Who filed a decision is the report tool's question, not the compiler's. |
| `prov:generatedAtTime` | no | Provenance. |
| `prov:wasGeneratedBy` | no | Provenance. |

## Version node — `rdf:type ledger:DecisionVersion`, keyed `urn:sha256:`

| Predicate | Read | Used for |
| --- | --- | --- |
| `ledger:ofDecision` | **yes** | Which decision this is a version of. |
| `ledger:set` | **yes** | Set membership, taken from the tip version. |
| `ledger:key` | **yes** | The nested type's name. Pending a ledger format change. |
| `ledger:statement` | **yes** | The generated doc comment. |
| `ledger:supersedes` | **yes** | Decision-to-decision. Marks the predecessor as having a successor. |
| `prov:wasRevisionOf` | **yes** | The revision chain. The version no other version revises is the tip. |

## Acceptance node — `rdf:type ledger:Acceptance`

| Predicate | Read | Used for |
| --- | --- | --- |
| `ledger:ofDecision` | **yes** | Attaches the acceptance to its decision directly. |
| `ledger:signsVersion` | **yes** | Which version was signed. |
| `ledger:scope` | carried, not acted on | `"version"`, or `"class:<ref>"`. See the open item below. |
| `prov:wasAttributedTo` | carried | The signing identity. |
| `prov:generatedAtTime` | carried | When. |
| `ledger:revokedAt` | **yes** | A revoked acceptance stops counting. |
| `ledger:revokedBy` | carried | Who revoked it. |
| `ledger:revocationReason` | carried | Why. |

### What "accepted" means

A decision is accepted when **at least one acceptance names the tip version in `ledger:signsVersion`
and carries no `ledger:revokedAt`**. Anything else is unaccepted, and unaccepted emits
`Obsolete(error: false)` (`DecisionsAsTypes.UnacceptedEmitsWarningObsolete`) — citable on a branch,
not shippable under warnings as errors.

## Open items for the ledger format

These are the places where the generator's behaviour is waiting on the format rather than settled.

1. **No decision-level retirement.** The ledger revokes acceptances, not decisions. So
   `DecisionsAsTypes.RevokedEmitsErrorObsolete` has nothing in the export to read, and is fed only
   by the interim front matter's `revoked-at`. A decision retired in the ledger today would read as
   *unaccepted* — a warning — rather than as revoked. Closing this needs a decision-level
   retirement predicate in the format.
2. **Class-scoped acceptance is not honoured.** An acceptance with `ledger:scope` of
   `"class:<ref>"` does not make a decision accepted here, because acceptance is decided by
   `signsVersion` naming the tip. This is the conservative reading: treating a class acceptance as
   covering the tip would let code ship citing a version nobody signed. If class scope is meant to
   confer acceptance, the format needs to say how a class resolves to versions.
3. **`ledger:key` is pending.** The key is a version-level field the format does not carry yet
   (`DecisionsAsTypes.VersionLevelKeyCarriedAcrossSupersession`). Until it does, the interim front
   matter is the only input that has one.

## What the report writes back

`DecisionDriven.Report` is the second read model over the ledger, and the only thing here that
writes to it: the citation projection is N-Triples for decision-cli to ingest
(`WholeGraphReport.CitationProjectionAsLedgerEntities`). It reads the ledger with the same two readers
the generator uses, linked into the tool, so the two cannot disagree about what the ledger says.
Four things it writes are this repository's assumptions until the format settles them:

1. **A citation's IRI is `urn:citation:` and a SHA-256** of its symbol, attribute and decision id.
   Stable across runs, so the same citation is the same node every time the report runs; a blank
   node would be a new citation on every ingest.
2. **`ledger:attribute` and `ledger:exceptionScope` are plain literals** holding the SKOS notation
   (`"Contract"`, `"Pool"`), not IRIs into a concept scheme, because the scheme's IRIs are not in the
   vocabulary yet.
3. **An interim version is a SHA-256 of the decision's own entry**: namespace, set, key and
   statement, joined by newlines. The front-matter reader synthesises one fixed id per decision
   (`urn:interim:<ns>/<key>`), which would make every citation cite the tip forever. Hashing the
   entry makes "cites a version that is no longer the tip" mean what it will mean with the export:
   the decision's content changed after the code cited it. Acceptance is not part of the hash; it
   is not content.
4. **A citation that cannot be dated gets no `ledger:citesVersion` and no `prov:wasGeneratedBy`**,
   rather than a guess. The Markdown summary lists those separately.

`prov:wasGeneratedBy` names the introducing commit as `urn:git:sha1:` or `urn:git:sha256:` by the
repository's object format, and the commit is typed `ledger:Commit` and `prov:Activity`
(`WholeGraphReport.LedgerCommitRequired`). Both types are asserted because the vocabulary names both
and the format has not yet said whether one entails the other.

## The interim form

Until `decision export --format ntriples` exists, a markdown set file carries the same model
(`DecisionsAsTypes.InterimFrontMatterUntilExport`):

```yaml
---
set: <kebab-case set id>
namespace: <ledger namespace>
decisions:
  - key: <PascalCase>
    statement: "<one line>"
    accepted-by: mailto:<identity>   # optional; absent = unaccepted
    accepted-at: <xsd:dateTime>      # required with accepted-by
    revoked-at: <xsd:dateTime>       # optional
---
```

`accepted-by` and `accepted-at` map onto `prov:wasAttributedTo` and `prov:generatedAtTime` on an
acceptance whose scope is `"version"`. In this form the key *is* the identity — there is no ULID —
so the decision id is synthesised as `dec:<namespace>/<key>` and two files claiming one key are
reported as `DDGEN0001` rather than silently merged.

## Names

A ledger set id and a ledger namespace are both "lowercase alphanumerics, dashes, dots", and neither
is a C# identifier. Both are PascalCased: `build-time-dependencies` is `BuildTimeDependencies`, and
namespace `ddd-analyzers` gives `DecisionDriven.Ledger.DddAnalyzers`. Decision keys are **not**
transformed — their syntax already makes them identifiers, and changing one would break the citation
it exists to carry.

That is why a key must not equal its set's PascalCased id, nor `SetId`: the set is a static class of
that name declaring a `SetId` constant, and a nested type named like its enclosing type (CS0542) or
like a sibling member (CS0102) is not valid C#. The generator reports such a key as `DDGEN0005`
against the ledger, and leaves that decision out so that the rest of the namespace still compiles,
rather than emitting code the compiler rejects in a file the consumer never wrote.
