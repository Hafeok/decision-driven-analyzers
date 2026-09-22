# ADR-A10: Open or closed hierarchies, never implicit (DD0017, DD0018)

**Status:** Proposed
**Date:** 2026-09-22
**Deciders:** Emil

## Context

The usual open/closed-principle proxy, "no type switches, use polymorphism", is wrong for this codebase. The SPARQL algebra, the RDF term model and the projection delta types are closed sets; the correct C# is a sealed hierarchy with exhaustive pattern matching, and the compiler checks exhaustiveness. The defect is a runtime-type switch over a hierarchy that is *not* sealed, which silently misses the next subtype.

## Decision

- **DD0017 Type switch over open hierarchy, tier 2.** A `switch` statement/expression or an `is`-pattern chain whose arms test for two or more subtypes of a common base is a warning unless the base's set of derived types is closed: an `abstract` base with a `private` or `file` constructor, or a base all of whose derived types in the compilation are `sealed` and the base is not `public`-derivable outside the assembly (`internal` constructor). False-positive story: switches over BCL open hierarchies (`Exception`, `Stream`) are excluded by default; a `default`/discard arm that throws is treated as an intentional partial switch and still warns, since that is exactly the missed-subtype case.
- **DD0018 No placeholder bodies, tier 1.** `throw new NotImplementedException()` anywhere in non-test code is an error. This also enforces the project rule that placeholder bodies must be explicit stubs listed at delivery; a stub is written as a `[DesignDecision]`-marked `NotSupportedException` with the ADR or issue reference, and DD0012 (ADR-A08) then tracks it.

Choosing which hierarchies are closed is a per-ADR design decision (e.g. the algebra ADR); this rule only forbids leaving it implicit.

## Alternatives considered

- Ban type switches outright. Rejected: fights the algebraic style the algebra and optimiser need.
- Require a visitor for every closed hierarchy. Rejected as a rule: visitors are one pattern for closed sets; exhaustive switches are another, and the compiler already enforces exhaustiveness for both when the hierarchy is sealed.

## Consequences

- Closed hierarchies get private/file constructors and sealed leaves as a matter of course; the analyzer reports where that has been forgotten.
- Stubs become visible in the ADR/issue index instead of in a grep for `NotImplementedException`.
