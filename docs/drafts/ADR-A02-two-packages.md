# ADR-A02: Generic rules in `DecisionDriven.Analyzers`, Varve rules in `Varve.Analyzers`

**Status:** Proposed
**Date:** 2026-09-22
**Deciders:** Emil

## Context

`Varve.Analyzers` exists with VARVE0001 (layer rule) and VARVE0002 (`InternalsVisibleTo`). Most of the rules agreed for Varve are not Varve-specific: layering by declared level, banned grab-bag names, static state, contract and model discipline. They will be reused across other decision-driven-design product lines. The only rules that know anything about Varve are the `[HotPath]` discipline and the fact that `Varve.Rdf` is the allowed contract vocabulary.

## Decision

Two analyzer packages.

- `DecisionDriven.Analyzers`: own repository (`hafeok/decision-driven-analyzers`, to move to an organisation later), MoM stewardship standard, MPL-2.0, published under the decision-driven-design NuGet organisation. Rule ids `DD0001` onward. Knows nothing about Varve. All configuration arrives through MSBuild properties surfaced with `CompilerVisibleProperty` and through `.editorconfig` options; a project never has to change analyzer code to adopt it.
- `Varve.Analyzers`: stays in the Varve repository, references the generic package, holds only Varve-specific rules (`VARVE0001` onward, renumbered as below).

Migration: VARVE0001 and VARVE0002 are re-implemented as DD rules (ADR-A04). `Varve.Analyzers` drops them once the generic package is referenced; the VARVE ids are retired, not reused. The ADR that introduced `Varve.Analyzers` is amended to reference this one.

Both packages are development-time only: `PrivateAssets="all"`, `IncludeAssets="analyzers;build"`, never a runtime dependency, so constraints 1 and 2 (managed-only, AOT) do not apply to their own code.

Configuration properties (all read via `CompilerVisibleProperty`):

| Property | Meaning |
|---|---|
| `ArchFamily` | Package-id prefix the layer rule applies to (`Varve`). |
| `ArchLayer` | Integer layer of this project. |
| `ArchCompositionRoot` | `true` for projects allowed to use service location and `new` of foreign concrete types. |
| `ArchContractTypeAssemblies` | Semicolon-separated assembly names whose types may appear on contract surfaces (ADR-A08). |

## Alternatives considered

- Everything in `Varve.Analyzers`. Rejected: rules would be copied per product line and drift.
- One package with a Varve mode. Rejected: violates high cohesion; the generic package would have a Varve reason to change.
- Attribute definitions in a separate runtime `Annotations` package. Rejected; see ADR-A07 for the source-generated alternative.

## Consequences

- A second repository with its own CI, release process and analyzer tests.
- `Varve.Analyzers` becomes small; most Varve enforcement is configuration of DD rules.
- Roslyn build-time dependencies for both packages need an ADR entry (ADR-A03).
