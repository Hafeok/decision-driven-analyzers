# ADR-A03: Build-time dependencies for the analyzer packages

**Status:** Proposed
**Date:** 2026-09-22
**Deciders:** Emil

> **Amended 2026-09-24.** The first consumer was named throughout this draft; this
> repository names no consumer (`CLAUDE.md`). Sentences that were about this package now
> say "the first consumer", and sentences that were only about that consumer's own code
> are deleted. Where this narrative and `docs/decisions/build-time-dependencies.md` disagree, the
> decision file governs.

## Context

Constraint 4 requires an ADR per third-party package. Analyzers need the Roslyn compiler packages to build, a test harness, and the off-the-shelf analyzers that cover rules we do not need to write ourselves. None of these ship in a consumer's runtime output.

## Decision

Allowed build-time packages, all `PrivateAssets="all"`:

| Package | Used by | Purpose |
|---|---|---|
| `Microsoft.CodeAnalysis.CSharp` (pinned to the lowest version supported by the current LTS SDK) | both analyzer projects | analyzer and source-generator API |
| `Microsoft.CodeAnalysis.Analyzers` | both analyzer projects | correctness of our own analyzers |
| `Microsoft.CodeAnalysis.CSharp.Analyzer.Testing` and `...SourceGenerators.Testing` (xUnit v3 verifier) | analyzer test projects | violating/conforming sample tests |
| `Microsoft.CodeAnalysis.PublicApiAnalyzers` | every shipped project | public API baseline; each new public member is a diff |
| `Microsoft.CodeAnalysis.BannedApiAnalyzers` | every shipped project | `BannedSymbols.txt`: reflection, `Reflection.Emit`, `dynamic`, and whatever a consumer adds |
| .NET SDK trimming, AOT and single-file analyzers (`EnableTrimAnalyzer`, `EnableAotAnalyzer`, `EnableSingleFileAnalyzer`) | every shipped project | constraint 2 |

All at error severity through `Directory.Build.props`. Off-the-shelf rules are preferred over a DD rule whenever they express the rule exactly; a DD rule is written only for what they cannot express.

## Alternatives considered

- Writing our own banned-symbol and public-API analyzers. Rejected: the Microsoft ones are exact and maintained.
- Reflection-based architecture tests (NetArchTest, ArchUnitNET) as the enforcement mechanism. Rejected per ADR-A01; ArchUnitNET may be adopted for tier-3 reports by a separate entry if the CI tool of ADR-A12 turns out to duplicate it.

## Consequences

- Version bumps follow the existing bump policy: patch/minor without amendment, major or new package with an entry here.
- The `BannedSymbols.txt` file is itself a tracked decision surface; adding a symbol needs an ADR citation in the file's comment line.
