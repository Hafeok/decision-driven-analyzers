# ADR-A04: Stable-dependency rules (DD0001–DD0003)

**Status:** Proposed
**Date:** 2026-09-22
**Deciders:** Emil

## Context

The stable dependencies principle in Varve is stated as a layered DAG: a project references only projects in strictly lower layers, contracts live in the lowest layer that can define them, and a lower layer never learns of a higher one through callbacks, service location or `InternalsVisibleTo`. Two of these are already enforced (VARVE0001, VARVE0002); they generalise, and the third is not yet enforced.

## Decision

Three tier-1 rules in the generic package.

- **DD0001 Layer reference.** A project with `ArchFamily=F` and `ArchLayer=n` may reference an assembly whose name starts with `F.` only if that assembly declares `ArchLayer < n`. The referenced layer is read from an assembly-level `[ArchLayer(n)]` attribute emitted by the source generator (ADR-A07) from the MSBuild property, so the check works across project and package references alike. An `F.*` reference without a declared layer is an error. Test assemblies (`*.Tests`) are exempt from being checked but still count as references for others.
- **DD0002 InternalsVisibleTo.** Every `InternalsVisibleTo` target must end in `.Tests`.
- **DD0003 Service location.** Calls to `IServiceProvider.GetService`, `GetRequiredService`, `GetKeyedService` and their extension-method forms, `ActivatorUtilities`, and `Activator.CreateInstance` are errors unless the project declares `ArchCompositionRoot=true`.

The callback loophole ("delegate typed to a higher-layer type") needs no rule: naming the higher type requires the reference DD0001 already forbids. The remaining smuggling channel, `object`-typed parameters on contracts, is closed by ADR-A08, and `dynamic` by the banned-symbols file.

## Alternatives considered

- Layer declared in `.editorconfig` per directory. Rejected: not visible across package references; the attribute in metadata is.
- Whole-solution reference-graph check in CI instead of a per-project analyzer. Rejected as the gate (ADR-A01); kept as the tier-3 instability report.

## Consequences

- Retires VARVE0001/VARVE0002 (ADR-A02).
- Every Varve project gets `ArchFamily` and `ArchLayer` in its project file; `Directory.Build.props` sets the family once.
- Hosts (`Varve.Server`, CLI) declare `ArchCompositionRoot=true`; nothing else does.
