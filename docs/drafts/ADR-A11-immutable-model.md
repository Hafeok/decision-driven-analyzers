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

Memoisation is not mutation, and DD0019 verifies it rather than reporting it. A cached hash, a lazily built immutable view or a per-key cache of a computed answer is written after the value is handed out, but every reader sees what the first would have computed. Computing it eagerly is measurably costly, or wrong when it is rarely needed; citing a `[DesignDecision]` for it records an exception to a rule that was never broken. A private field marked `[Memoized]` is checked against two forms:

- **a value memo**: not readonly, of an immutable type, written only by `field ??= expr` or `if (field == sentinel) field = expr` (or the same through a local copy), in one member, from an expression of readonly state, parameters and calls the body check finds change nothing;
- **a per-key memo**: a readonly `ConcurrentDictionary<K, V>` of immutable values, filled only through `GetOrAdd` with a static lambda or static method as the factory.

A plain `Dictionary`, `HashSet` or `List` is refused even when every write is guarded. A model value is shared by whoever holds it, and two readers filling a plain collection at once is a race that corrupts it; `ConcurrentDictionary.GetOrAdd` is the one fill the framework makes safe. The factory has to be static so that it cannot capture: a lambda over `this` or a local would make what is memoised for a key depend on who asked first. `[Memoized]` cites no decision, because the rule checks the claim. Anything that is not one of the two forms is still a finding, and its message says which way out was taken.

## Alternatives considered

- Records only. Rejected: `readonly struct` and `ref struct` are needed for allocation-free paths and are not all records.
- Immutability by convention with review. Rejected by ADR-A01.
- `Lazy<T>` for every memo. Rejected as the only form: it allocates a `Lazy<T>` and a delegate per value, which is the eager cost moved rather than removed, and a cached hash is the case where that matters. It remains available, since DD0004 already judges a `Lazy<T>` of an immutable type immutable.
- `[DesignDecision]` on each memo. Rejected: it records an exception to a rule the memo does not break, and an unchecked exception admits a memo that does break it.

## Consequences

- Datasets and graphs that mutate in place live outside `[DomainModel]` namespaces (they are stores, not values), which is consistent with the log-first model where mutation is a commit.
- Builder types are explicit and never leak across a contract.
- A model class may hold a mutable collection privately, fill it in its constructor and only read it afterwards; that is as immutable as a `FrozenSet` and cheaper to write. DD0019 tells reads from writes by the read-only collection interfaces the held type implements, plus a short list of reads those interfaces leave out. Anything it cannot classify, including a call through an interface it does not know, is treated as a write: a false positive costs a decision, a false negative costs the snapshot.
