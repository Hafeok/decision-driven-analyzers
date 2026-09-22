# ADR-A05: No mutable static state or static registries (DD0004)

**Status:** Proposed
**Date:** 2026-09-22
**Deciders:** Emil

## Context

Low coupling in Varve forbids shared mutable state between packages and static registries. Static registries are also the usual way a lower layer discovers a higher one without a reference, which defeats ADR-A04.

## Decision

**DD0004, tier 1.** Errors on:

- a static field or property that is not `readonly`/init-only, or is `readonly` but of a mutable type (any collection type, `StringBuilder`, anything with a public setter, `Lazy<T>` of a mutable type);
- static constructors and static initialisers that register into another static (the registry pattern), detected as a static field of a collection or delegate-list type written from any member;
- `[ThreadStatic]` and `AsyncLocal<T>` statics.

Exempt: `static readonly` fields of immutable types, `const`, compiled `Regex`, `Encoding` instances, `ArrayPool<T>.Shared` / `MemoryPool<T>.Shared` references, and pooling caches declared with `[DesignDecision]` (ADR-A07) citing the ADR that introduces the pool. Test assemblies are exempt.

## Alternatives considered

- Allow-list of assemblies instead of per-member `[DesignDecision]`. Rejected: an assembly-wide exemption hides new statics inside an already-exempt assembly.
- Banning only registries and allowing plain mutable statics. Rejected: plain mutable statics are the concurrency defect, registries are the coupling defect; both are out.

## Consequences

- Buffer pools and interning caches in the term dictionary are declared and cited, which is where the decision belongs anyway.
- No ambient configuration; everything is constructor-injected or passed as a parameter.
