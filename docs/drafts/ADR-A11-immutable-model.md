# ADR-A11: Domain model types are immutable by default (DD0019)

**Status:** Proposed
**Date:** 2026-09-22
**Deciders:** Emil

## Context

Terms, quads, positions, commits and algebra nodes are values. Snapshot isolation and as-of reads are only trustworthy if the values handed out by a pinned read cannot be mutated by the caller. Mutable model types also break the record-based equality the property tests rely on.

## Decision

**DD0019, tier 1.** In `[DomainModel]` namespaces, for public types:

- no public or internal setters other than `init`;
- no fields other than `readonly`;
- no public members typed as a mutable collection (`List<T>`, `Dictionary<K,V>`, `T[]` as a property, `ICollection<T>`, `IList<T>`, `ISet<T>`); expose `ImmutableArray<T>`, `IReadOnlyList<T>`, `ReadOnlyMemory<T>`, `ReadOnlySpan<T>` or `FrozenDictionary`/`FrozenSet`;
- structs are `readonly`.

Builders are the escape hatch: a type named `*Builder` in the same namespace is exempt, must be `sealed`, and must not be exposed by any `[Contract]` member (DD0010 already prevents it since a builder is neither model vocabulary nor a contract).

## Alternatives considered

- Records only. Rejected: `readonly struct` and `ref struct` are needed for allocation-free paths and are not all records.
- Immutability by convention with review. Rejected by ADR-A01.

## Consequences

- Datasets and graphs that mutate in place live outside `[DomainModel]` namespaces (they are stores, not values), which is consistent with the log-first model where mutation is a commit.
- Builder types are explicit and never leak across a contract.
