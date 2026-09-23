# DDBUILD0001: Do not move the Roslyn pin without moving the floor

|  |  |
| --- | --- |
| **Id** | `DDBUILD0001` |
| **Tier** | 1 (error) |
| **Severity** | error (an MSBuild error; the build does not produce a package) |
| **Motivating decision** | `BuildTimeDependencies.RoslynPinEnforcedByBuild` |
| **Introduced in** | unreleased |

## Principle

The version of Roslyn the analyzers are built against decides which SDKs can load them, so
it is a statement about who the package supports — not a dependency version that tracks
whatever is newest.

## What the rule checks

`DDBUILD0001` is a build target, not a Roslyn analyzer. There is no compilation to inspect:
the thing being checked is the build's own inputs, and by the time a compilation exists the
wrong Roslyn has already been chosen. `RuleTiers.BuildTargetChecksAreTierOne` makes that an
accepted tier-1 form, under the `DDBUILD` id family so it is never mistaken for a `DDnnnn`
diagnostic a consumer could see.

The target runs before `CoreCompile` in `src/DecisionDriven.Analyzers` and compares the
central pins against the floor that project declares:

| Pinned in `Directory.Packages.props` | Must equal |
| --- | --- |
| `Microsoft.CodeAnalysis.CSharp` | `$(ExpectedRoslynVersion)` |
| `Microsoft.CodeAnalysis.CSharp.Workspaces` | `$(ExpectedRoslynVersion)` |

Any difference is an error naming both versions and the decision.

The floor lives in the analyzer project and the pins live in `Directory.Packages.props`
deliberately. A dependency bot edits the second file and not the first, so a bump it
proposes cannot satisfy both and the build stops.

## Configuration

| Setting | Where | Default | Effect |
| --- | --- | --- | --- |
| `ExpectedRoslynVersion` | `src/DecisionDriven.Analyzers/DecisionDriven.Analyzers.csproj` | `5.0.0` | The Roslyn version the pins must equal |

There is nothing for a consumer to configure: this rule runs on this repository's own
build and is not part of the shipped package. Raising the floor means editing
`ExpectedRoslynVersion` and both pins in the same commit, and saying in the commit message
which SDK band the new floor belongs to.

## False-positive story

Tier 1, and decidable: the check compares two strings that are both in the tree, so it
cannot be wrong about what it read. It can be wrong about what it *should* read, in one
case — when the floor is genuinely meant to move, because the pinned LTS SDK band moved.
That is not a false positive, it is the rule asking for the decision to be made explicitly,
and the fix is to change `ExpectedRoslynVersion` and the pins together.

The check does not verify that the floor is in fact the lowest Roslyn the pinned SDK band
ships — that claim is `BuildTimeDependencies.RoslynPinnedToLowestSupported`, and it was
established by running `csc -version` from the lowest SDK of the band. This rule only
guarantees the tree keeps agreeing with the number that exercise produced.

## Violating example

`Directory.Packages.props`, as proposed by a dependency bot:

```xml
<PackageVersion Include="Microsoft.CodeAnalysis.CSharp" Version="5.9.0" />
<PackageVersion Include="Microsoft.CodeAnalysis.Analyzers" Version="5.9.0" />
```

```text
DDBUILD0001: Microsoft.CodeAnalysis.CSharp is pinned to 5.9.0, but this project expects
5.0.0. BuildTimeDependencies.RoslynPinnedToLowestSupported pins Roslyn to the lowest
version the current LTS SDK band ships, so that the analyzers load on every SDK in the
band rather than only the newest. If the floor is meant to move, change
ExpectedRoslynVersion in this project and the pin in Directory.Packages.props in the same
commit.
```

This is not hypothetical: it is the diff of a real dependency-bot pull request, which
passed CI before this rule existed.

## Conforming example

```xml
<PackageVersion Include="Microsoft.CodeAnalysis.CSharp" Version="5.0.0" />
<PackageVersion Include="Microsoft.CodeAnalysis.CSharp.Workspaces" Version="5.0.0" />
```

with

```xml
<ExpectedRoslynVersion>5.0.0</ExpectedRoslynVersion>
```

The pins and the floor agree, so the analyzers load on every SDK in the .NET 10 band,
including the oldest — which CI never exercises and therefore could never have caught.

## See also

- `docs/decisions/build-time-dependencies.md` — `RoslynPinEnforcedByBuild`, and the
  `RoslynPinnedToLowestSupported` claim it protects
- `docs/decisions/rule-tiers.md` — `BuildTargetChecksAreTierOne`
- `docs/drafts/ADR-A03-build-time-dependencies.md` — the narrative behind the package list
- `.github/dependabot.yml` — the three ignored packages, and why an ignore rule alone was
  not enough
