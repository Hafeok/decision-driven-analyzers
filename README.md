# DecisionDriven.Analyzers — repo bundle

DecisionDriven.Analyzers is a development-time NuGet package of Roslyn analyzers that
enforce decision-driven design in a C# solution: every architectural rule the analyzers
check exists because a decision in `docs/decisions/` says so, and the diagnostic points
back at that decision by key. The package carries the analyzers and the MSBuild props
that let a consumer describe its own architecture (`ArchFamily`, `ArchLayer`,
`ArchCompositionRoot`, `ArchContractTypeAssemblies`) without changing analyzer code;
a companion tool, `DecisionDriven.Report`, produces the whole-graph reports that a
single compilation cannot. It is referenced with `PrivateAssets=all` and never appears
in a consumer's runtime output.

Status: pre-release, nothing published yet.

## Rules

| Id | Tier | Rule | Decision |
| --- | --- | --- | --- |
| [DD0001](docs/rules/DD0001.md) | 1 | A reference within a family points strictly downward | `StableDependencyRules.LayerReferenceStrictlyDownward` |
| [DD0002](docs/rules/DD0002.md) | 1 | Every `InternalsVisibleTo` target is a test assembly | `StableDependencyRules.InternalsVisibleToTestsOnly` |
| [DD0003](docs/rules/DD0003.md) | 1 | Services are resolved only in the composition root | `StableDependencyRules.NoServiceLocationOutsideCompositionRoot` |
| [DDBUILD0001](docs/rules/DDBUILD0001.md) | 1 | The Roslyn pin matches the floor the analyzers declare | `BuildTimeDependencies.RoslynPinEnforcedByBuild` |
| [DDBUILD0002](docs/rules/DDBUILD0002.md) | 1 | The package carries the code-fixes assembly | `TwoPackages.CodeFixesShipInSeparateAssembly` |

Id families are `RuleTiers.IdFamilies`: `DD` for Roslyn analyzers, `DDBUILD` for build-target
checks, `DDGEN` for generator diagnostics. What the generator reads is
[`docs/rules/ledger-input.md`](docs/rules/ledger-input.md).

Drop into the empty repository root, run `PROMPT-bootstrap.md` in Claude Code, then `PROMPT-session-1.md`.

- `docs/drafts/` — narrative decision records (ADR-A01…A14 minus A13). Kept as narrative; not what code cites.
- `docs/decisions/` — interim set files, one per draft, in the front-matter form of decisions-as-types. Each decision has a key and a one-line statement and no acceptance. These are what the generator reads and what code cites as `typeof(<Set>.<Key>)` until decision-cli exports the ledger.
- `CLAUDE.md` — repository conventions for agents.
- `PROMPT-session-1.md` — the build prompt.

Interim set file format (read by the generator until the N-Triples export exists):

```yaml
---
set: <kebab-case set id>          # becomes the PascalCase static class name
namespace: <ledger namespace>     # becomes DecisionDriven.Ledger.<namespace>
decisions:
  - key: <PascalCase, ^[A-Z][A-Za-z0-9]{0,63}$>
    statement: "<one line>"
    accepted-by: mailto:<identity>   # optional; absent = unaccepted = Obsolete(error: false)
    accepted-at: <xsd:dateTime>      # required with accepted-by
    revoked-at: <xsd:dateTime>       # optional; present = Obsolete(error: true)
---
```

Set ids: lowercase alphanumerics, dashes, dots (ledger set id rules). Keys unique per namespace across all set files.
