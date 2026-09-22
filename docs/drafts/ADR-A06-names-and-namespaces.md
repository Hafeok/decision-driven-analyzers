# ADR-A06: Banned grab-bag names and namespace–assembly alignment (DD0005, DD0006)

**Status:** Proposed
**Date:** 2026-09-22
**Deciders:** Emil

## Context

High cohesion in Varve means one package, one reason to change. `Common`, `Core`, `Utils`, `Helpers` and `Abstractions` packages are where the second reason to change accumulates. Cohesion metrics (LCOM family) are too noisy to gate a build and penalise span-based code by construction, so the only honest cohesion gate is on names and package shape.

## Decision

- **DD0005, tier 1.** No assembly, root namespace or namespace segment named `Common`, `Core`, `Utils`, `Utilities`, `Helpers`, `Abstractions`, `Shared`, `Misc`, `Internal` (as a public namespace) or `Extensions` (as a namespace; `*Extensions` static classes are fine). The list is configurable in `.editorconfig` (`dd_banned_names`).
- **DD0006, tier 1.** Every public type's namespace starts with the assembly name, and an assembly has exactly one root namespace equal to its name. Public types outside that root are errors.

LCOM and similar per-type cohesion metrics go to the tier-3 report (ADR-A12).

## Alternatives considered

- Method-length or member-count thresholds as an SRP proxy. Rejected: they measure size, not responsibility, and they are what people suppress first.
- Allowing `Abstractions` packages. Rejected: contracts live in the lowest layer that can define them; a separate abstractions package is that layer with a worse name.

## Consequences

- Shared low-level helpers are either duplicated when small or get their own cohesive package by ADR, as the project prompt already states; the analyzer makes the third option unavailable.
