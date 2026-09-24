# ADR-A07: Decisions as types from the Decision Ledger; typed architecture attributes; suppressions (DD0007, DD0008)

**Status:** Proposed
**Date:** 2026-09-22
**Deciders:** Emil

> **Amended 2026-09-24.** DD0008's rule below named a specific consumer's id prefix; it now reads
> "product-prefix rules". Where this narrative and `docs/decisions/decisions-as-types.md` disagree,
> the decision file governs: the drafts were written before the repository and are narrative, not
> the decision.

## Context

Rules need markers in source: which interfaces are contracts, which namespaces are domain model, which members are hot paths, which violations are accepted decisions. A string citation names a document, not a decision, is checked only after the fact, and lets code exist without a decision as long as the string parses. C# attribute arguments must be compile-time constants (primitives, `string`, `Type`, enums, arrays), so binding has to be built from `Type`.

Decisions already have a home: the Decision Ledger (decision-cli), vocabulary `ledger:` (`urn:ledger:ns#`). A decision is `dec:<ns>/<ulid>`, versioned by content hash (`urn:sha256:`), grouped into sets with human ids, accepted per version by identities holding a role with `accept-decision`, superseded decision-to-decision, revoked by triples on the node, with invalidation and `waive-invalidation` already defined. The ledger files are the truth; the graph is a read model. The generator here is another read model.

## Decision

### The ledger is the source; the generator emits a read model

An incremental source generator in `DecisionDriven.Analyzers` reads an N-Triples export of the ledger (`docs/decisions/<namespace>.nt`, produced by `decision export --format ntriples`, committed, passed as `AdditionalFiles`) and emits into each consuming compilation:

```csharp
namespace DecisionDriven.Ledger.<namespace>;

internal static class LedgerDesign                      // ledger set id "ledger-design", PascalCased
{
    public const string SetId = "ledger-design";

    public static class QuadSourceContract              // decision key (version-level, see below)
    {
        public const string Id = "dec:varve/01K5…";
        public const string Key = "QuadSourceContract";
        public const string Namespace = "varve";
    }

    [Obsolete("Not accepted: no unrevoked acceptance of the tip version", error: false)]
    public static class RawIdOnDictionaryContract { … }

    [Obsolete("Revoked 2026-10-02: …", error: true)]
    public static class SixtyFourBitIds { … }
}
```

Rules of emission:

- One type per set; membership is the decision's tip version's `ledger:set`.
- One nested type per decision, named by its key. The key is a **version-level, hashed field** unique per (namespace, key), immutable across versions of one decision and carried across supersession. Syntax `^[A-Z][A-Za-z0-9]{0,63}$`. This is a ledger format change ([OPEN] in ledger-format-v1) and sits before this ADR; immutability across versions is a file-gate check, uniqueness across live decisions in a namespace is a graph-stage SPARQL shape.
- Supersession is **not** obsolescence: the key carries to the successor, code keeps compiling, and the recorded citation version (below) diverges from the tip, which is the ledger's own invalidation. Revocation without successor → `[Obsolete(error: true)]`.
- No unrevoked acceptance of the tip version → `[Obsolete(error: false)]`. Branch code compiles with a warning; release builds (`TreatWarningsAsErrors`) do not. Because the ledger's file gate refuses a model as holder, a decision an agent reconstructs during adoption becomes citable in release code only after a human with `accept-decision` accepts it.
- The generated namespace is prefixed by ledger namespace so a repository citing several namespaces never collides. The ledger namespace is PascalCased the same way a set id is, because a ledger namespace is "lowercase alphanumerics, dashes, dots" and a C# namespace segment is not: `ddd-analyzers` becomes `DecisionDriven.Ledger.DddAnalyzers`.
- Nothing about a citation's version is written in source. A hash literal in the attribute would restate a ledger fact and go stale on revision. The version cited is derived by the report tool (ADR-A12): blame the citing symbol to its introducing commit, resolve the decision's tip at that commit.

Interim input: until the ledger format carries keys and decision-cli can export, the generator also reads markdown front matter (`set`, `namespace`, `key`, `statement`, `accepted-by`, `accepted-at`) mapped one-to-one onto the same model. decision-cli imports those files into ledger files and they are deleted. The front matter is not a schema and not a restatement, since until import nothing in the ledger holds those facts.

### Typed attributes

The same generator emits the marker attributes as `internal sealed` classes in namespace `DecisionDriven`, matched by full name across assemblies, not `[Conditional]`.

| Attribute | Target | Signature |
|---|---|---|
| `ArchLayer` | assembly | `(int layer)`; from the `ArchLayer` MSBuild property; consumed by DD0001 |
| `Contract` | interface, abstract class, delegate | `(Type decision)` + `string Role` (required named) |
| `DomainModel` | assembly | `(string namespacePrefix, Type decision)` |
| `HotPath` | method, property, type | `(Type decision)` |
| `DesignDecision` | any | `(Type decision)` + `ExceptionScope Scope` (required named) |

`ExceptionScope` is a generated enum, closed: `Boundary`, `HotPath`, `Pool`, `Interop`, `Compatibility`, `Migration`. Its values are the notations of a SKOS scheme in the ledger vocabulary so the report tool emits the same tokens. `Role` is the one free string and is a label, not a justification.

```csharp
[Contract(typeof(LedgerDesign.QuadSourceContract), Role = "store read side")]
public interface IQuadSource { … }

[DesignDecision(typeof(LedgerDesign.RawIdOnDictionaryContract), Scope = ExceptionScope.Boundary)]
ulong Id { get; }
```

### Rules

- **DD0007, tier 1.** The `decision` argument must be a generator-emitted decision type (a hand-written type of that shape is an error); required named arguments must be present; `[DomainModel]` prefixes must be namespaces of the current assembly. A citation of a decision that does not exist is already a compile error.
- **DD0008, tier 1.** `#pragma warning disable` and `[SuppressMessage]` for any `DD*` or product-prefix rule are errors; `[DesignDecision]` on the offending symbol is the only way to accept a violation. `.editorconfig` severity downgrades of a DD rule below its declared tier are reported with no exception path: changing a rule's tier is a superseding decision in the generic repository, not a consumer setting.

## Alternatives considered

- String citations with regex and a CI existence check. Rejected: names the document, not the decision; checked after the fact.
- `new Adr(...)` arguments. Not expressible in C#.
- A private `dd:` vocabulary aligned to the ledger later. Rejected once the ledger vocabulary was seen: two vocabularies for one thing, alignment as permanent maintenance.
- Enum members per decision with `nameof`. Rejected in favour of nested types: one `Type` argument, no string, and constants per decision.
- Version hash pinned in the attribute. Rejected: restates a ledger fact; derived from git instead.
- Turtle input. Rejected: the generator is dependency-free `netstandard2.0`; N-Triples is trivially readable, Turtle is not, and a consumer's own Turtle parser cannot be used without a cycle.
- Runtime `Annotations` package; marker interfaces. Rejected as before (runtime dependency; public-surface change).

## Consequences

- Code cannot cite a decision that has not been filed as a decision; release code cannot cite one no human has accepted.
- Every citation is a symbol reference: find-all-references on a decision type lists the code depending on it.
- The ledger export is a build input; a malformed export breaks every project in the repository, which is intended.
- Ledger format changes required before this lands: version-level key; `ledger:Commit` (ADR-A12). Both are ledger decisions, filed there.
- The generic repository's own decisions live in the ledger under their own namespace; the analyzers cite themselves.
