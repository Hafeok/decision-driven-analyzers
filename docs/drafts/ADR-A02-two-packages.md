# ADR-A02: Generic rules here, product-specific rules in the product's own package

**Status:** Proposed
**Date:** 2026-09-22
**Deciders:** Emil

> **Amended 2026-09-24.** The first consumer was named throughout this draft; this repository names
> no consumer (`CLAUDE.md`). Sentences that were about this package now say "the first consumer",
> and sentences that were only about that consumer's own migration bookkeeping are deleted. Where
> this narrative and `docs/decisions/two-packages.md` disagree, the decision file governs.

## Context

The first consumer already has an analyzer package of its own, with two rules in it: a layer rule and an `InternalsVisibleTo` rule. Most of the rules it has agreed are not specific to it at all - layering by declared level, banned grab-bag names, static state, contract and model discipline - and they will be reused across other decision-driven-design product lines. The only rules that know anything about that consumer are its `[HotPath]` discipline and the fact that its own model assemblies are its allowed contract vocabulary.

## Decision

Two analyzer packages.

- `DecisionDriven.Analyzers`: own repository (`hafeok/decision-driven-analyzers`, to move to an organisation later), MoM stewardship standard, MPL-2.0, published under the decision-driven-design NuGet organisation. Rule ids `DD0001` onward. Knows nothing about any consumer. All configuration arrives through MSBuild properties surfaced with `CompilerVisibleProperty` and through `.editorconfig` options; a project never has to change analyzer code to adopt it.
- A product's own analyzer package: stays in that product's repository, references this one, and holds only the rules that know something about the product, under its own id prefix.

Migration for a product that already has rules of its own: the generic equivalents are re-implemented here (ADR-A04), the product package drops its copies once it references this one, and the retired ids are never reused.

Both packages are development-time only: `PrivateAssets="all"`, `IncludeAssets="analyzers;build"`, never a runtime dependency, so constraints 1 and 2 (managed-only, AOT) do not apply to their own code.

Configuration properties (all read via `CompilerVisibleProperty`):

| Property | Meaning |
|---|---|
| `ArchFamily` | Package-id prefix the layer rule applies to. |
| `ArchLayer` | Integer layer of this project. |
| `ArchCompositionRoot` | `true` for projects allowed to use service location and `new` of foreign concrete types. |
| `ArchContractTypeAssemblies` | Semicolon-separated assembly names whose types may appear on contract surfaces (ADR-A08). |

## Alternatives considered

- Everything in the product's own package. Rejected: rules would be copied per product line and drift.
- One package with a per-product mode. Rejected: violates high cohesion; the generic package would have a second reason to change, one per product.
- Attribute definitions in a separate runtime `Annotations` package. Rejected; see ADR-A07 for the source-generated alternative.

## Consequences

- A second repository with its own CI, release process and analyzer tests.
- A product's own package becomes small; most of its enforcement is configuration of DD rules.
- Roslyn build-time dependencies for both packages need an ADR entry (ADR-A03).
