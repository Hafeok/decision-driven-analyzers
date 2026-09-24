# ADR-A09: Primitive-free model and contract surfaces (DD0013–DD0015)

**Status:** Proposed
**Date:** 2026-09-22
**Deciders:** Emil

> **Amended 2026-09-24.** The first consumer was named throughout this draft; this
> repository names no consumer (`CLAUDE.md`). Sentences that were about this package now
> say "the first consumer", and sentences that were only about that consumer's own code
> are deleted. Where this narrative and `docs/decisions/primitive-free-surfaces.md` disagree, the
> decision file governs.

## Context

The defect class being guarded against is `Read(long position, ulong graphId)`: two silently swappable primitives standing in for two distinct domain concepts. Applied to every public method in every package the rule breaks the parse/format boundary (`Iri.Parse(ReadOnlySpan<char>)`) and the hot paths, which are spans by design. Applied to the model and contract surfaces it forces the model: `Position`, `TermId`, `Iri`, `CommitTimestamp`.

## Decision

Scope: public members (parameters and return types) of types in `[DomainModel]` namespaces, and all `[Contract]` members.

- **DD0013 No naked primitives on model or contract surfaces, tier 1.** Banned types in scope: `string`, `char`, `sbyte`/`byte`/`short`/`ushort`/`int`/`uint`/`long`/`ulong`/`nint`/`nuint`, `float`/`double`/`decimal`/`Half`, `Guid`, `DateTime`, `DateTimeOffset`, `TimeSpan`, `DateOnly`, `TimeOnly`, `object`, and arrays or `IEnumerable<T>`/`IReadOnlyList<T>` of these. List configurable via `dd_banned_primitive_types`.
  Not banned: `bool` as a return type; `ReadOnlySpan<T>`, `Span<T>`, `Memory<T>`, `ReadOnlyMemory<T>` of any element type; `CancellationToken`; enums; generic type parameters; `Task`/`ValueTask` wrappers of allowed types.
  Boundary exemption: members named `Parse`, `TryParse`, `Format`, `TryFormat`, `Create`-from-primitive factories on the wrapper type itself, implementations of `ISpanParsable<T>`/`ISpanFormattable`/`IUtf8SpanParsable<T>`/`IUtf8SpanFormattable`, and `[HotPath]` members. `[DesignDecision]` for anything else.
- **DD0014 Wrapper shape, tier 1.** A type in a `[DomainModel]` namespace that wraps a single banned primitive (one field or auto-property of a banned type) must be a `readonly record struct` or a `readonly struct` with value equality. A class wrapper is an error: it would allocate on every index scan (constraint 5).
- **DD0015 No implicit primitive conversions, tier 1.** No `implicit operator` to or from a banned primitive on a `[DomainModel]` type. Explicit operators and named accessors (`.Value`) are allowed. An implicit conversion reintroduces the swappable-argument defect while satisfying DD0013.
- **Flag arguments, tier 2 (DD0016).** A `bool` parameter on a `[Contract]` member or public `[DomainModel]` member is a warning; prefer an enum. False-positive story: `TryX(out ...)` patterns and predicate delegates are excluded; the remaining hits are usually real.

## Alternatives considered

- Apply to every public method in every package. Rejected: parsers, hot paths and BCL-shaped APIs would lie or be wrapped in ceremony.
- Define "primitive" as "not a class or struct". Rejected: `int` is a struct; the rule needs a type list.
- Allow class wrappers when immutable. Rejected by constraint 5.

## Consequences

- The model and contract namespaces are declared `[DomainModel]`; parsing and scanning internals are not.
- Existing signatures with `long position`, `string iri` or `ulong id` on contract surfaces become errors on adoption; the fix is the wrapper type the ADR set zero id-scheme ADR already implies.
