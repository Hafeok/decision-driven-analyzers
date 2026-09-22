# ADR-A01: Every rule is an analyzer, in one of three tiers

**Status:** Proposed
**Date:** 2026-09-22
**Deciders:** Emil

## Context

The Varve project rule is that an architecture or code rule exists only if it is enforced by a Roslyn analyzer that fails the build. Applied without qualification this produces two failure modes: heuristics promoted to build errors that fire false positives, which trains people to suppress; and whole-graph properties (instability, afferent coupling) that an analyzer cannot compute because it sees one compilation at a time.

## Decision

Every rule is classified into exactly one tier at the time it is agreed, and the tier is part of the rule's doc page.

- Tier 1, error: mechanical, decidable within one compilation, no judgement involved. Fails the build. Suppression requires an ADR citation (ADR-A07).
- Tier 2, warning: a heuristic with a stated false-positive story written into its doc page. A tier-2 rule is promoted to tier 1 only by a superseding ADR after it has run on a real codebase without false positives.
- Tier 3, report: whole-graph metrics computed by a CI tool over built assemblies (ADR-A12). Never a gate until a baseline exists and an ADR sets the threshold.

The deliverable for a new rule, in order of importance: the analyzer, its tests (one violating and one conforming sample per diagnostic), the doc page naming the tier, the principle served, the false-positive story (tier 2), and the ADR that motivates it.

## Alternatives considered

- All rules at error severity. Rejected: heuristics at error severity make suppression routine and destroy the signal of the mechanical rules.
- Rules as documentation with review-time enforcement. Rejected by the existing Varve rule.
- Architecture tests (ArchUnitNET-style) instead of analyzers. Rejected as the primary mechanism because they run after the build, need a test project referencing every package, and cannot fail an individual project's compile. Retained for tier 3.

## Consequences

- Every rule proposal must argue its tier; "is this checkable in one compilation without judgement" becomes the first question.
- Tier 2 rules accumulate a track record before they can gate; this slows enforcement of heuristics on purpose.
- Tier 3 needs a tool and a CI job (ADR-A12).
