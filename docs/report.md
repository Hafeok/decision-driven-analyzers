# DecisionDriven.Report

The tier-3 half of `RuleTiers.ThreeTiers`: what a single compilation cannot see. An analyzer runs on
one project at a time; instability is a property of the whole reference graph, a contract's
divergence is about every caller in every assembly, and when a citation was written is a fact about
the repository's history. This tool reads all three.

It is a report. `WholeGraphReport.NothingGatesUntilBaselineDecision`: the exit code says whether a
report could be produced, never what it found. A metric becomes a gate only by a decision that names
its threshold and the baseline it was measured against.

```
decisiondriven-report --assembly <file-or-directory> [--assembly ...]
                      [--repo <dir>] [--ledger <dir>] [--since <ref>] [--out <dir>]
```

| Option | Default | |
| --- | --- | --- |
| `--assembly` | — | A built assembly, or a directory whose `*.dll` are read. Repeatable. Duplicates by name are read once. |
| `--repo` | `.` | The repository root, for git history. |
| `--ledger` | `docs/decisions` | The ledger directory, relative to the repository. `*.md` set files and `*.nt` exports are both read. |
| `--since` | — | A ref. Decisions first cited in a commit not reachable from it are listed as newly cited. |
| `--out` | `artifacts/report` | Where `report.md` and `citations.nt` are written. |

With no arguments, or with `--version`, it prints its version. Exit codes: `0` a report, `1` inputs
that could not be read, `2` a command line that could not be parsed.

Assemblies are read as metadata through `System.Reflection.Metadata` and never loaded, so the tool
needs none of a consumer's dependencies. The portable PDB, embedded or beside the assembly, is how a
citing symbol is traced to its source file.

## Layers

Per assembly that declares `[ArchLayer]`: afferent coupling (Ca), efferent coupling (Ce),
instability `I = Ce / (Ca + Ce)`, abstractness `A`, and distance from the main sequence
`D = |A + I − 1|`.

- Coupling is counted between the layered assemblies given, from the assembly references their
  metadata carries. The compiler writes a reference only for an assembly whose types are used, so an
  unused project reference does not count.
- Abstractness is interfaces and non-static abstract classes over all types somebody wrote. The
  generator's marker attributes and decision types, compiler-generated types and embedded attributes
  are excluded; counting them would make every assembly's abstractness a measure of the generator.
- An assembly is marked when something at a higher layer is more stable than it. DD0001 guarantees
  references point down; this is the check that the layers are the right ones.

## Contracts

Per `[Contract]` interface: its members, its implementers across every assembly given, and which
members each calling type uses. `Contracts.MemberCountIsReport`: member count alone measures the
wrong thing, so the report shows what each caller takes of what the contract offers. A call inside a
lambda is credited to the type that wrote the lambda, not to its compiler-generated closure.

## Model cohesion

LCOM4 for each type in a `[DomainModel]` namespace: the number of groups its methods fall into, where
two methods are one group when they touch a common field or one calls the other. A method that
reaches a property through its getter touches the property's field; accessors themselves are not
counted, and neither are the members a compiler writes for a record. A type with no methods is data
and has no row.

## Citations

One per symbol carrying `[Contract]`, `[DomainModel]`, `[HotPath]` or `[DesignDecision]`, emitted to
`citations.nt` as a `ledger:Citation` with `ledger:ofDecision`, `ledger:citesVersion`,
`ledger:symbol` (the documentation-comment id — `N:` and the namespace for `[DomainModel]`),
`ledger:attribute`, `ledger:exceptionScope` and `prov:wasGeneratedBy` the introducing commit.

- **The cited decision** is read from the generated type's `Id`, `Key` and `Namespace` constants, the
  identity the compiler checked, rather than re-derived from names.
- **The introducing commit** is the oldest commit that changed how often the cited key appears in
  the symbol's source file (`git log -S`), falling back to the first commit that touched the file.
  An interface has no method bodies and so no sequence points; its file comes from the
  `TypeDefinitionDocuments` record the compiler writes for exactly that case. With no file at all,
  the key is searched for across the repository outside the ledger directory.
- **The version it cites** is the decision's tip as the ledger stood at that commit, read from the
  commit rather than the working tree. `DecisionsAsTypes.CitationVersionDerivedNotWritten`: this is
  where a citation's version comes from, because writing it in source would restate a ledger fact
  and go stale.

The Markdown summary lists decisions nothing cites, citations of a version that is no longer the
tip, decisions newly cited since `--since`, and citations that could not be dated. The assumptions
the projection makes about the ledger format are in
[`docs/rules/ledger-input.md`](rules/ledger-input.md#what-the-report-writes-back).

## In CI

The samples job installs the tool from the run's own nupkg — the same bytes that would be
published — runs it over the conforming samples, and appends `report.md` to the job summary.
`--since` is the pull request's base, or the commit a push replaced.
