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
using DecisionDriven.Ledger.Catalog;

[Contract(typeof(CatalogShape.ProductLookupContract), Role = "catalog read side")]
public interface IProductLookup
{
    Product? Find(ProductId id);
}
```

cites this, in `docs/decisions/catalog-shape.md`:

```yaml
---
set: catalog-shape
namespace: catalog
decisions:
  - key: ProductLookupContract
    statement: "The catalog's read side is one contract, and it is narrow"
    accepted-by: mailto:someone@example.com
    accepted-at: 2026-01-14T09:00:00Z
---
```

Remove the `accepted-by` and the same code still compiles, with a warning on every citation. That is
the whole mechanism.

## Quick start

**Declare `[DomainModel]` before adding `[Contract]`.** A contract may name the framework, the
assemblies in `ArchContractTypeAssemblies`, other contracts, and this assembly's declared model —
and nothing else. An assembly that has not said which of its namespaces are the model has answered
that question for none of them, so the first `[Contract]` you add reports every type in its
signatures at once (DD0010). One assembly-level line ahead of time is the difference:

```csharp
[assembly: DomainModel("Consumer.Model", typeof(CatalogShape.ModelNamespace))]
```

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
| [DD0007](https://github.com/Hafeok/decision-driven-analyzers/blob/main/docs/rules/DD0007.md) | 1 | A cited decision is a generated type, and required named arguments are present | shipped |
| [DD0008](https://github.com/Hafeok/decision-driven-analyzers/blob/main/docs/rules/DD0008.md) | 1 | No `#pragma warning disable`, `[SuppressMessage]` or severity downgrade for a DD rule | shipped |
| [DD0009](https://github.com/Hafeok/decision-driven-analyzers/blob/main/docs/rules/DD0009.md) | 1 | Every public interface, abstract class and delegate carries `[Contract]` | shipped |
| [DD0010](https://github.com/Hafeok/decision-driven-analyzers/blob/main/docs/rules/DD0010.md) | 1 | Contract signatures use only the allowed type vocabulary | shipped |
| [DD0011](https://github.com/Hafeok/decision-driven-analyzers/blob/main/docs/rules/DD0011.md) | 1 | Contract parameters are data, not ad-hoc collaborators | shipped |
| [DD0012](https://github.com/Hafeok/decision-driven-analyzers/blob/main/docs/rules/DD0012.md) | 1 | No `NotSupportedException` from an implemented contract member | shipped |
| [DD0013](https://github.com/Hafeok/decision-driven-analyzers/blob/main/docs/rules/DD0013.md) | 1 | No naked primitives on model and contract surfaces | shipped |
| [DD0014](https://github.com/Hafeok/decision-driven-analyzers/blob/main/docs/rules/DD0014.md) | 1 | A wrapper around one primitive is a readonly struct with value equality | shipped |
| [DD0015](https://github.com/Hafeok/decision-driven-analyzers/blob/main/docs/rules/DD0015.md) | 1 | No implicit conversions to or from a banned primitive | shipped |
| [DD0016](https://github.com/Hafeok/decision-driven-analyzers/blob/main/docs/rules/DD0016.md) | 2 | A `bool` parameter on a contract is a warning; prefer an enum | shipped |
| [DD0017](https://github.com/Hafeok/decision-driven-analyzers/blob/main/docs/rules/DD0017.md) | 2 | A type switch over an open hierarchy is a warning | shipped |
| [DD0018](https://github.com/Hafeok/decision-driven-analyzers/blob/main/docs/rules/DD0018.md) | 1 | No `NotImplementedException` outside tests | shipped |
| [DD0019](https://github.com/Hafeok/decision-driven-analyzers/blob/main/docs/rules/DD0019.md) | 1 | Public domain model types are immutable | shipped |
| [DDBUILD0001](https://github.com/Hafeok/decision-driven-analyzers/blob/main/docs/rules/DDBUILD0001.md) | 1 | The Roslyn pin matches the floor the analyzers declare | shipped |
| [DDBUILD0002](https://github.com/Hafeok/decision-driven-analyzers/blob/main/docs/rules/DDBUILD0002.md) | 1 | The package carries its code-fixes assembly | shipped |
| [DDGEN0001-0004](https://github.com/Hafeok/decision-driven-analyzers/blob/main/docs/rules/ledger-input.md) | — | The decision input is well formed | shipped |

Id families: `DD` for Roslyn analyzers, `DDBUILD` for build-target checks, `DDGEN` for generator
diagnostics.

### Decisions

Every rule, attribute and tool here is a decision in [`docs/decisions/`](https://github.com/Hafeok/decision-driven-analyzers/blob/main/docs/decisions), one set per file,
all unaccepted (see [Status](#status)).

| Set | Decides |
| --- | --- |
| [`rule-tiers`](https://github.com/Hafeok/decision-driven-analyzers/blob/main/docs/decisions/rule-tiers.md) | What a rule is, its tier, and what it ships with |
| [`two-packages`](https://github.com/Hafeok/decision-driven-analyzers/blob/main/docs/decisions/two-packages.md) | This package and a product package: ids, configuration, code fixes, development-time only |
| [`build-time-dependencies`](https://github.com/Hafeok/decision-driven-analyzers/blob/main/docs/decisions/build-time-dependencies.md) | The Roslyn pin, the test harness, versions from git tags |
| [`decisions-as-types`](https://github.com/Hafeok/decision-driven-analyzers/blob/main/docs/decisions/decisions-as-types.md) | The ledger as source, the generator as read model, citations as type references |
| [`diagnostic-messages`](https://github.com/Hafeok/decision-driven-analyzers/blob/main/docs/decisions/diagnostic-messages.md) | The finding, `Decide:` with both paths, and the guard sentence |
| [`stable-dependency-rules`](https://github.com/Hafeok/decision-driven-analyzers/blob/main/docs/decisions/stable-dependency-rules.md) | Layers, `InternalsVisibleTo`, service location (DD0001-DD0003) |
| [`static-state`](https://github.com/Hafeok/decision-driven-analyzers/blob/main/docs/decisions/static-state.md) | No mutable static state (DD0004) |
| [`names-and-namespaces`](https://github.com/Hafeok/decision-driven-analyzers/blob/main/docs/decisions/names-and-namespaces.md) | Grab-bag names and root namespaces (DD0005, DD0006) |
| [`contracts`](https://github.com/Hafeok/decision-driven-analyzers/blob/main/docs/decisions/contracts.md) | `[Contract]`, its vocabulary and its parameters (DD0009-DD0012) |
| [`primitive-free-surfaces`](https://github.com/Hafeok/decision-driven-analyzers/blob/main/docs/decisions/primitive-free-surfaces.md) | Wrappers instead of naked primitives, and flag arguments (DD0013-DD0016) |
| [`hierarchies`](https://github.com/Hafeok/decision-driven-analyzers/blob/main/docs/decisions/hierarchies.md) | Closed hierarchies and stubs (DD0017, DD0018) |
| [`immutable-model`](https://github.com/Hafeok/decision-driven-analyzers/blob/main/docs/decisions/immutable-model.md) | An immutable domain model, with builders as the escape hatch (DD0019) |
| [`whole-graph-report`](https://github.com/Hafeok/decision-driven-analyzers/blob/main/docs/decisions/whole-graph-report.md) | What `DecisionDriven.Report` computes, and that none of it gates |
| [`release-process`](https://github.com/Hafeok/decision-driven-analyzers/blob/main/docs/decisions/release-process.md) | A published package is the tested package, and a tag publishes only through the checks |

Whole-graph metrics — instability, abstractness, contract divergence, LCOM4 — are not rules. They
belong to `DecisionDriven.Report`, and nothing gates on them until a decision names a threshold and
the baseline it was measured against. See
[`docs/report.md`](https://github.com/Hafeok/decision-driven-analyzers/blob/main/docs/report.md).

```
dotnet tool install --global DecisionDriven.Report --prerelease
decisiondriven-report --assembly <bin-dir> --since origin/main
```

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

**Pre-release.** Prereleases are published to nuget.org under the `decision-driven-design`
organisation; see the [changelog](https://github.com/Hafeok/decision-driven-analyzers/blob/main/CHANGELOG.md)
for what each one carries. Versions are `0.x` and the public surface may change without ceremony
until `1.0`.

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
