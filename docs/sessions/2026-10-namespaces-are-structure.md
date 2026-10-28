# Session close-out: namespaces as structure, 0.1.0-preview.12

28 October 2026. Started on `main` at `9eecbf5` (`v0.1.0-preview.11`). Ends with `0.1.0-preview.12`.

## Rule ids

`DD0022` (a namespace declaration names the file's folder) and `DD0023` (the namespaces of a
layered compilation form a DAG), both registered in `AnalyzerReleases.Unshipped.md`. `DD0021`
stays reserved for the Discharges session and `DiagnosticIds.cs` says so. The next free DD id is
`DD0024`.

## Test counts

| | Total |
| --- | --- |
| Before (`9eecbf5`) | 580 |
| After step 2, DD0005 (`bd7f0f2`) | 594 |
| After step 3, DD0022 (`be9ff4d`) | 623 |
| After step 4, DD0023 (`1339174`) | 670 |
| After step 5, report (`bdcb547`) | 672 |

- Step 2 added 14 to `BannedNameTests`: the six plural names as a segment (rows in the existing
  theory), as a root namespace and as an assembly name segment, `Model` and `Services` silent at
  both, and the override covering the new names.
- Step 3 added 29: `NamespaceFolderTests` (25: block and file-scoped, the project root, nested
  folders, `RootNamespace` set and empty, global types of three kinds, an invalid folder, a linked
  file including the `<project>/../Other/File.cs` form the compiler hands over, dot segments, two
  generated forms, Windows paths, case, no `ProjectDir`, nested blocks, the exception path with the
  partial case, the exact message, and `ProjectDir`/`RootNamespace` through a real analyzer config
  file) and `NamespaceFolderFixTests` (4).
- Step 4 added 47 in `NamespaceCycleTests`: the cycles and the DAG, 34 reference-kind rows, nested
  types, `using` only, generated code with its control, no layer, the exception path, the first
  location, the exact message, and the timing test.
- Step 5 added 2 in `MetricsTests`.

The packaged samples check reports 23 ids: 21 once, DD0019, DD0022 and DD0023 twice each, and
DDBUILD0001 once; the conforming build is silent.

## DD0023 timing

`A_few_hundred_types_across_many_namespaces_are_analysed_in_bounded_time`: 401 types in 40
namespaces, every type referring to two types and creating one object in the next namespace, plus
one edge closing a 40-namespace ring so that the report path runs too. Three runs on the session's
container, measured around `RuleHarness.Run`, which parses and compiles the source as well as
running the rule:

| Run | ms |
| --- | ---: |
| 1 | 1779 |
| 2 | 1631 |
| 3 | 1630 |

No threshold is set. The number is the whole harness, not the rule alone; the test asserts only
that every edge of the ring is reported.

## PRs

| Issue | PR | Decision | Merged as |
| --- | --- | --- | --- |
| #124 | #125 | `NamesAndNamespaces.BannedGrabBagNames` (new version), `NamespaceMatchesFolder`, `NamespaceDepthNotRequired`, `StableDependencyRules.NamespacesAcyclicWithinAssembly`, `WholeGraphReport.NamespaceShapeInReport` (all new), ADR-A17, amendments to A06, A04, A12 | squash `46faa45` |
| #126 | #128 | `NamesAndNamespaces.BannedGrabBagNames` (new version) | squash `bd7f0f2` |
| #127 | #130 | `NamesAndNamespaces.NamespaceMatchesFolder` | squash `be9ff4d` |
| #129 | #131 | `StableDependencyRules.NamespacesAcyclicWithinAssembly` | squash `1339174` |
| #132 | #133 | `WholeGraphReport.NamespaceShapeInReport` | squash `bdcb547` |
| #134 | #135 | `ReleaseProcess.TagPublishesOnlyThroughGate` | squash `4f6b1b4` |

Every new or amended key is unaccepted. No `accepted-by` was added.

## Release

| | Run | Result |
| --- | --- | --- |
| `ci` on `4f6b1b4` (#135 merged), before tagging | [40674849230](https://github.com/Hafeok/decision-driven-analyzers/actions/runs/40674849230) | green: build (ubuntu, windows), format, samples, `ci` |
| `publish` started by `v0.1.0-preview.12` | not started | waits on the tag push below |

`v0.1.0-preview.12` is a signed annotated tag on `4f6b1b4`, created in this session. Pushing it was
refused with HTTP 403 on `git-receive-pack`, the same as in the last four sessions, and the push was
not routed around. **The maintainer pushes the tag.** It can be recreated from `4f6b1b4` with
`git tag -s v0.1.0-preview.12 4f6b1b4 -m "0.1.0-preview.12: namespaces as structure (DD0005 v2, DD0022, DD0023, report)"`.
Then approve the `nuget` environment for the `publish` run, and check that nuget.org lists
`0.1.0-preview.12` for both `DecisionDriven.Analyzers` and `DecisionDriven.Report`. No manual
`publish` run was started, so nothing was published alongside. The earlier `ci` runs: `46faa45`
(#125) [40666632037](https://github.com/Hafeok/decision-driven-analyzers/actions/runs/40666632037),
`bd7f0f2` (#128) [40668361551](https://github.com/Hafeok/decision-driven-analyzers/actions/runs/40668361551),
`be9ff4d` (#130) [40671998348](https://github.com/Hafeok/decision-driven-analyzers/actions/runs/40671998348),
`1339174` (#131) [40672556823](https://github.com/Hafeok/decision-driven-analyzers/actions/runs/40672556823),
`bdcb547` (#133) [40673358217](https://github.com/Hafeok/decision-driven-analyzers/actions/runs/40673358217),
all green.

## What moved in this repository

DD0023, run over `src/`, found one cycle: `DecisionDriven.Analyzers` and
`DecisionDriven.Analyzers.Generation` referred to each other. `DecisionLedgerGenerator.cs` (root
namespace) used `GeneratedAttributes`, and `GeneratorIdentity` took `typeof(DecisionLedgerGenerator)`
to derive the generated tree path prefix. The generator is the type that closed the cycle, and it
moved to `Generation/DecisionLedgerGenerator.cs` in namespace `DecisionDriven.Analyzers.Generation`.
The generated files' directory under `obj/` changes with its full name; `docs/rules/DD0007.md`, the
two tests that spell the path and the map in `CLAUDE.md` follow. `DecisionDriven.Analyzers.CodeFixes`
and `DecisionDriven.Report` are acyclic (one edge and none on a cycle).

How it was run: the analyzer assembly cannot be loaded as a loose `<Analyzer>` into its own
project's build (the compiler refuses an analyzer with the compilation's own name, and a loose copy
from outside the repository fails to load), so the analyzer project was checked through the test
harness over its source files, and the code-fixes and report projects by a build with the analyzer
DLL staged under `artifacts/` and `ArchLayer=1`. That build also fired DD0022 on the linked files
those projects compile, which was the bug below.

## The samples restructuring

Every violating file now sits in the folder its namespace names, so DD0022 is silent on all but
one of them:

- `Violations/Utils/DD0005.cs` declares `Sample.Layer2.Violations.Utils`: DD0005 still fires on
  `Utils`.
- `Violations/Model/DD0014.cs`, `DD0015.cs`, `DD0019.cs` declare `Sample.Layer2.Violations.Model`,
  and `_Model.cs` declares that prefix as `[DomainModel]` (the configuration moved, not the rules).
  `Violations/Model/Stores/_Stores.cs` holds the sub-namespace the declaration leaves out.
- `Violations/DD0006.cs` keeps `Elsewhere`, outside the root, and is the one file DD0022 also
  reports; `violations.expected` says `DD0022 2` with a comment, and `check-violations.sh` now
  skips `#` comments.
- `Violations/DD0022.cs` declares `Sample.Layer2.Elsewhere`, under the root, so only DD0022 reports
  it; `Violations/DD0023/Ping/` and `Pong/` refer to each other for DD0023.

## Rulings made inside the prompt's scope

- **A namespace block that holds only nested blocks is not itself reported by DD0022.** Every type
  ends up in some innermost declaration, and that one is checked by its full name. Without this,
  `namespace Sample.Layer2 { namespace Ingest { ... } }` in `Ingest/` would be a false positive.
- **DD0022's code fix renames a nested block by its own part only**, and only when the expected name
  lies under the containing block; otherwise no fix is offered. The placeholder fix is not offered
  for DD0022, because the exception path is `[DesignDecision]` on every type in the declaration and
  a namespace declaration cannot carry the attribute; the page says so.
- **DD0022 collapses `.` and `..` segments** in both the path and `ProjectDir` before comparing.
  The compiler makes a linked file's path absolute by prefixing the project directory and nothing
  more, so `<project>/../Other/File.cs` was reported for lying in a folder named `..`. Found by the
  self-check above and fixed in #130 before it merged.
- **DD0023's message names one shortest path back**, by breadth-first search inside the component,
  `Sample.B -> Sample.C -> Sample.A`, so a three-namespace cycle reads the same way from each edge.
- **A `const` field reference is an edge** like any member reference; a test row says so, after the
  self-check raised the question.
- **The report counts a nested type in its outermost type's namespace**, the node DD0023 attributes
  it to, and public means public at every level of nesting.
- **The samples' `[DomainModel]` prefix moved with the files** rather than the model-surface rules
  being touched, as the prompt ruled.

## Questions

1. **The JSON baseline.** The prompt says to add the namespace shape to the committed JSON baseline
   shape. `DecisionDriven.Report` writes `report.md` and `citations.nt` only; there is no JSON
   output and no committed baseline in the repository, although ADR-A12 names one. The section is in
   the Markdown report and in the row model. The options are (i) a `report.json` beside `report.md`
   with every section, committed as a baseline from the samples job, which is a step of its own with
   a decision about its shape, or (ii) leaving the report Markdown-only until a metric gates.
2. **Running the rules on this repository's own projects.** `CLAUDE.md` says the analyzers run on
   the analyzers, but no project in `src/` or `tests/` loads them, and the self-check above had to
   be done by hand. It found, besides the DD0022 bug: DD0004 on `DiagnosticIds.IdFamilies`
   (`string[]`, a static mutable collection), and DD0017 twice in `DesignDecisionPlaceholderFix`
   (switches over `CSharpSyntaxNode`, which nothing closes). Neither is this session's scope. The
   options are (i) a self-consumption step that loads the packed analyzers into `src/` and `tests/`
   with `ArchLayer` set and fixes or decides each finding, or (ii) leaving it. The DD0008 findings
   on `obj/` files in that run were an artefact of the hand-rolled load, which lacked the package
   props that make `IntermediateOutputPath` visible.
3. **DD0022 and `Directory.Build.props`-style shared files.** A file compiled into several projects
   by a `<Compile Include="../Shared/X.cs">` is a linked file and is not checked in any of them.
   That is what the decision says, and it means a shared file's namespace is checked nowhere. The
   options are (i) to leave it, or (ii) a later rule or report row listing files no project checks.

## Found along the way

- The force-push needed to reset the session branch onto `main` after each squash merge was refused
  as destructive, as in earlier sessions, so each step's PR came from its own branch
  (`claude/wizardly-bardeen-rflp96-<n>-<rule>`) cut from `main`; the session branch holds steps 1
  and 2 as pushed and was not reused after that.
- `dotnet test` under Microsoft.Testing.Platform does not surface `ITestOutputHelper` output in its
  summary, so the timing above was read through a temporary file write that is not committed.

## Deferred

- Nothing from the prompt.
