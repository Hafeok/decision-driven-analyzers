# DecisionDriven.Analyzers

Roslyn analyzers that make every public contract, model type and accepted exception cite a decision
the compiler can see. A citation is a type reference rather than a comment or an issue number, so
code cannot exist without a decision behind it, and release code cannot cite a decision no human has
accepted: an unaccepted decision is generated as obsolete, and a build with warnings as errors will
not ship it.

## The model

- A **decision set** becomes a generated static class; a **decision** becomes a nested static class
  named by its key.
- Code cites one with `[Contract(typeof(<Set>.<Key>))]`, `[DomainModel(...)]`, `[HotPath(...)]`, or
  `[DesignDecision(typeof(<Set>.<Key>), Scope = …)]` to accept a violation.
- `[assembly: ArchLayer(n)]` is generated from an MSBuild property and read back out of metadata, so
  layering is checked across package references as well as project references.
- A decision with no unrevoked acceptance of its tip version is `[Obsolete(error: false)]`: citing
  it is **CS0618**, a warning.
- A revoked decision with no successor is `[Obsolete(error: true)]`: citing it is **CS0619**, an
  error. Supersession is neither — the key carries to the successor and the code keeps compiling.

```csharp
using DecisionDriven;
using DecisionDriven.Ledger.Varve;

[Contract(typeof(StoreShape.QuadSourceContract), Role = "store read side")]
public interface IQuadSource
{
    int Read(QuadWindow window);
}
```

cites this, in `docs/decisions/store-shape.md`:

```yaml
---
set: store-shape
namespace: varve
decisions:
  - key: QuadSourceContract
    statement: "The store's read side is one contract, and it is narrow"
    accepted-by: mailto:someone@example.com
    accepted-at: 2026-01-14T09:00:00Z
---
```

Remove the `accepted-by` and the same code still compiles, with a warning on every citation. That is
the whole mechanism.

## Quick start

```xml
<ItemGroup>
  <PackageReference Include="DecisionDriven.Analyzers" Version="0.1.0-*" PrivateAssets="all" />
</ItemGroup>

<PropertyGroup>
  <ArchFamily>Sample</ArchFamily>
  <ArchLayer>1</ArchLayer>
  <ArchCompositionRoot>false</ArchCompositionRoot>
  <ArchContractTypeAssemblies>Sample.Contracts</ArchContractTypeAssemblies>
  <DdLedgerDirectory>$(MSBuildThisFileDirectory)../../docs/decisions</DdLedgerDirectory>
</PropertyGroup>
```

`PrivateAssets="all"` is not optional: the package is development-time only and never appears in a
consumer's runtime output. The four `Arch*` properties are the whole configuration surface — adopting
a rule never means changing analyzer code.

Write one decision set file, then `dotnet build`. With no decisions the analyzers load and say
nothing; with a decision set, the types become citable.

### The decision set format

Until the ledger can export, a markdown file with YAML front matter carries the decisions. This is
the interim form and is deleted once `decision export` exists.

```yaml
---
set: <kebab-case set id>          # becomes the PascalCase static class name
namespace: <ledger namespace>     # becomes DecisionDriven.Ledger.<PascalCase>
decisions:
  - key: <PascalCase, ^[A-Z][A-Za-z0-9]{0,63}$>
    statement: "<one line>"
    accepted-by: mailto:<identity>   # optional; absent = unaccepted = Obsolete(error: false)
    accepted-at: <xsd:dateTime>      # required with accepted-by
    revoked-at: <xsd:dateTime>       # optional; present = Obsolete(error: true)
---
```

Set ids are lowercase alphanumerics, dashes and dots. Keys are unique per namespace across all set
files; a key claimed twice is `DDGEN0001`.

The N-Triples path is implemented and has no producer yet: a file tagged
`DdLedger="ledger-export"` is read as the ledger's export, and nothing emits one today. See
[the ledger seam](https://github.com/Hafeok/decision-driven-analyzers/blob/main/README.md#the-ledger-seam).

## Rules

| Id | Tier | Rule | Status |
| --- | --- | --- | --- |
| [DD0001](https://github.com/Hafeok/decision-driven-analyzers/blob/main/docs/rules/DD0001.md) | 1 | A reference within a family points strictly downward | shipped |
| [DD0002](https://github.com/Hafeok/decision-driven-analyzers/blob/main/docs/rules/DD0002.md) | 1 | Every `InternalsVisibleTo` target is a test assembly | shipped |
| [DD0003](https://github.com/Hafeok/decision-driven-analyzers/blob/main/docs/rules/DD0003.md) | 1 | Services are resolved only in the composition root | shipped |
| [DD0004](https://github.com/Hafeok/decision-driven-analyzers/blob/main/docs/rules/DD0004.md) | 1 | No mutable static state, including the static registry | shipped |
| [DD0005](https://github.com/Hafeok/decision-driven-analyzers/blob/main/docs/rules/DD0005.md) | 1 | No grab-bag name on an assembly or a namespace | shipped |
| [DD0006](https://github.com/Hafeok/decision-driven-analyzers/blob/main/docs/rules/DD0006.md) | 1 | Public types live under the assembly's root namespace | shipped |
| DD0007 | 1 | A cited decision is a generated type, and required named arguments are present | planned |
| DD0008 | 1 | No `#pragma warning disable` or `[SuppressMessage]` for a DD rule | planned |
| DD0009 | 1 | Every public interface, abstract class and delegate carries `[Contract]` | planned |
| DD0010 | 1 | Contract signatures use only the allowed type vocabulary | planned |
| DD0011 | 1 | Contract parameters are data, not ad-hoc collaborators | planned |
| DD0012 | 1 | No `NotSupportedException` from an implemented contract member | planned |
| DD0013 | 1 | No naked primitives on model and contract surfaces | planned |
| DD0014 | 1 | A wrapper around one primitive is a readonly struct with value equality | planned |
| DD0015 | 1 | No implicit conversions to or from a banned primitive | planned |
| DD0016 | 2 | A `bool` parameter on a contract is a warning; prefer an enum | planned |
| DD0017 | 2 | A type switch over an open hierarchy is a warning | planned |
| DD0018 | 1 | No `NotImplementedException` outside tests | planned |
| DD0019 | 1 | Public domain model types are immutable | planned |
| [DDBUILD0001](https://github.com/Hafeok/decision-driven-analyzers/blob/main/docs/rules/DDBUILD0001.md) | 1 | The Roslyn pin matches the floor the analyzers declare | shipped |
| [DDBUILD0002](https://github.com/Hafeok/decision-driven-analyzers/blob/main/docs/rules/DDBUILD0002.md) | 1 | The package carries its code-fixes assembly | shipped |
| [DDGEN0001-0004](https://github.com/Hafeok/decision-driven-analyzers/blob/main/docs/rules/ledger-input.md) | — | The decision input is well formed | shipped |

Id families: `DD` for Roslyn analyzers, `DDBUILD` for build-target checks, `DDGEN` for generator
diagnostics.

Whole-graph metrics — instability, abstractness, contract divergence, LCOM4 — are not rules. They
belong to `DecisionDriven.Report`, and nothing gates on them until a decision names a threshold and
the baseline it was measured against.

## Diagnostics

Every message has the same shape:

```
<what was found>. Decide: <design-change path> | <documented-exception path>. <guard>
```

The messages are long by analyzer standards, deliberately. A description and a help link are
IDE-only; whoever reads `dotnet build` output — a person or an agent — sees the id and the message
and nothing else. A message that said only what was wrong would make the shortest path to green
"add the attribute it mentions", which is what the guard sentence exists to stop.

### Applying fixes

`dotnet build` never runs a code fix. From a terminal:

```
dotnet format analyzers --diagnostics DD0002
```

Two kinds are offered. The design-change fix exists only where it is mechanical, and it reaches
green. The documented-exception fix inserts

```csharp
[DesignDecision(typeof(____.____), Scope = ExceptionScope.____)]
```

which does not compile. That is the point: the build stays red with one remaining error naming
exactly what is missing, and no fix reaches green without either a design change or a filed,
accepted decision.

## The ledger seam

Decisions live in a Decision Ledger. The generator is a read model over its export and never a
second source of truth.

**Today** it reads markdown set files, tagged `DdLedger="decision-set"` on `AdditionalFiles`, in the
format above.

**When `decision-cli` can export**, it reads N-Triples tagged `DdLedger="ledger-export"` —
`ledger:Decision`, `ledger:DecisionVersion` and `ledger:Acceptance` nodes, with acceptance decided
by `ledger:signsVersion` naming the tip and no `ledger:revokedAt`. That reader is implemented and
untested against a real export, because none exists.

[`docs/rules/ledger-input.md`](https://github.com/Hafeok/decision-driven-analyzers/blob/main/docs/rules/ledger-input.md) records exactly what is read, and three
open items where the generator's behaviour is waiting on the format rather than settled — chiefly
that the ledger has no decision-level retirement, so a revoked decision can currently only be
expressed in the interim form.

## Status

**Pre-release. Nothing is published yet.** Versions are `0.x` and the public surface may change
without ceremony until `1.0`.

Every decision in this repository's own `docs/decisions/` is **unaccepted**, and will stay that way
until a human with the authority accepts it in the ledger — that is the point of the mechanism, not
an oversight. For a consumer it means: citing any of them produces CS0618 on every citation, so they
are usable on a branch and will not ship under `TreatWarningsAsErrors`. Your own decisions, in your
own ledger, are yours to accept.

Versions come from git tags via MinVer. A `v*` tag is a release; every other build of the trunk is a
prerelease.

Licensed under [MPL-2.0](https://github.com/Hafeok/decision-driven-analyzers/blob/main/LICENSE). Contributions are under the DCO — sign off with `git commit -s`;
there is no CLA. See [CONTRIBUTING.md](https://github.com/Hafeok/decision-driven-analyzers/blob/main/CONTRIBUTING.md) for how to add a rule, and
[GOVERNANCE.md](https://github.com/Hafeok/decision-driven-analyzers/blob/main/GOVERNANCE.md) for who accepts a decision and why that is a human act.
