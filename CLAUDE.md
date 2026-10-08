# CLAUDE.md — DecisionDriven.Analyzers

Roslyn analyzers, a source generator and a report tool that make design decisions compile-time obligations. Code cites decisions as types (`[Contract(typeof(<Set>.<Key>))]`); the generator emits those types from a decision ledger; the analyzers refuse code that contradicts a decision and refuse citations of decisions nobody accepted. This repository is its own first consumer: the analyzers run on the analyzers.

Read `docs/decisions/` and `docs/drafts/` before changing anything. Every rule, attribute and tool here is a decision in `docs/decisions/`; if what you are about to do has no decision behind it, the decision comes first.

## Map

| Path | What |
|---|---|
| `src/DecisionDriven.Analyzers/Rules/` | one analyzer per rule, `Descriptors.cs` and `DiagnosticIds.cs` for ids and messages, `ArchOptions.cs` for everything read from MSBuild |
| `src/DecisionDriven.Analyzers/Ledger/` | `NTriplesReader` (the ledger export), `FrontMatterReader` (interim set files), the shared model |
| `src/DecisionDriven.Analyzers/Generation/`, `DecisionLedgerGenerator.cs` | the generator: one static class per set, nested class per key, the marker attributes, `[Obsolete]` for unaccepted and revoked |
| `src/DecisionDriven.Analyzers.CodeFixes/` | code fixes, a separate assembly because an analyzer assembly may not reference Workspaces (RS1038) |
| `src/DecisionDriven.Analyzers.Package/` | the `.props`/`.targets` the nupkg ships: `CompilerVisibleProperty` for the `Arch*` settings, `AdditionalFiles` with `DdLedger` metadata |
| `src/DecisionDriven.Report/` | tier-3 whole-graph metrics and the citation projection (`ledger:Citation` N-Triples) |
| `docs/decisions/` | decision sets, interim front-matter format; what code cites |
| `docs/drafts/` | the ADR narratives (A01–A14) behind the sets |
| `docs/rules/` | one page per diagnostic; `_template.md` and `README.md` say what a page owes |
| `docs/rules/ledger-input.md` | the reader contract: exactly what the generator reads from an export |
| `samples/Consumer/` | a consumer built the way a consumer builds; everything outside `Violations/` conforms |
| `tests/` | `Microsoft.CodeAnalysis.Testing` for rules and generator; `RuleHarness.cs` is the entry point |

## Conventions

- Current LTS .NET SDK from `global.json`. Analyzer and generator projects target netstandard2.0 with no dependencies beyond the Roslyn packages pinned in `Directory.Packages.props` (`BuildTimeDependencies.RoslynPinnedToLowestSupported`, enforced by DDBUILD0001). The report tool targets the LTS TFM.
- Diagnostic id families: `DD` analyzers, `DDBUILD` build targets, `DDGEN` generator (`RuleTiers.IdFamilies`). Ids are never reused. New rules take the next free `DD` id; check `AnalyzerReleases.Shipped.md` and `Unshipped.md`, and register every new or changed descriptor in `Unshipped.md` (RS2000-series checks fail otherwise).
- Diagnostic messages follow `diagnostic-messages`: the finding, `Decide:` with the design-change path and the documented-exception path, then the guard sentence. The exception path is always `[DesignDecision(typeof(<Set>.<Key>), Scope = ExceptionScope.<Scope>)]`. Messages are tested verbatim.
- Tiers (`rule-tiers`): 1 = mechanical, single compilation, error; 2 = heuristic, warning, with a written false-positive story; 3 = whole graph, lives in the report, never gates without a baseline. A rule's tier is fixed when the decision is agreed.
- Generated code is exempt from every rule except DD0008 (`RuleTiers.GeneratedCodeIsExempt`); "generated" means produced by a generator in this compilation, `*.g.cs`/`*.generated.cs`/`*.designer.cs`, or in the intermediate output directory. Nothing else counts, whatever its header says.
- Never add `accepted-by` to a decision. Acceptance is a human act in the ledger.
- Never leave a `typeof(____.____)` placeholder in the tree at the end of a session.
- Never use `#pragma warning disable`, `[SuppressMessage]` or an `.editorconfig` downgrade for DD rules; that is DD0008, and DD0008's descriptor is `NotConfigurable` so it cannot be turned off either.
- Nothing in this repository mentions any consumer: not in code, tests, docs, changelog, commits or pull requests. Issues filed by consumers may name themselves; the fix never does.

## Adding or changing a rule

In this order, none optional (`RuleTiers.RuleDeliverableOrder`):

1. The decision: a key in the right set under `docs/decisions/`, unaccepted; a narrative change in the matching `docs/drafts/ADR-*.md` when the reasoning changes. A changed statement is a new version of the key.
2. The analyzer, citing the decision from its descriptor.
3. Tests: a violating and a conforming sample, the exact message, and every configuration arm. Tests for citation rules (DD0007 and anything reading generated types) start from a real generator run, never hand-written lookalikes.
4. The rule page `docs/rules/DDnnnn.md` from `_template.md`: id, tier, principle, motivating decision, configuration with the unset case, false-positive story, both examples with the diagnostic quoted verbatim.
5. `AnalyzerReleases.Unshipped.md`, the README rules table, `CHANGELOG.md` under `[Unreleased]` with the issue link.
6. A conforming case in `samples/Consumer` where the rule has a configuration surface, and a violating case under `Violations/` guarded by `DD_SAMPLE_VIOLATIONS` (`RuleTiers.SamplesJobIsTheFalsePositiveCheck`).
7. A code fix only where one is mechanical; the placeholder fix is shared and must emit code that does not compile.

## Tests: what the harness does and does not cover

- `RuleHarness.OptionsFor(...)` feeds `AnalyzerConfigOptions` from a dictionary. It bypasses Roslyn's config parser. Any property that carries a list or special characters must also be tested through `RuleHarness.VerifyThroughAnalyzerConfigAsync`, which writes a real analyzer config file so the parser runs. Roslyn's parser treats `;` and `#` in a property value as the start of a comment; the package's `.targets` rewrites `ArchContractTypeAssemblies` to `_DecisionDrivenArchContractTypeAssemblies` with `,` for that reason (#85).
- Cross-assembly rules (DD0001, DD0017) compile referenced projects to real metadata in the harness, because attributes read from metadata behave differently from source. An assembly-level attribute must be the first element in its file; the generator gives it its own file, and so does the harness.
- The generated marker types are `Embedded`, so `InternalsVisibleTo` never sees a second copy (#51). Tests that span two compilations must keep that property.
- The samples job in CI is the only place the packed `.props`/`.targets` run. A fix in them is not covered until the sample consumer exercises it.
- `AnalyzerPackage.cs` holds the package-layout tests: both assemblies under `analyzers/dotnet/cs`, props and targets under `build/`.

## The ledger seam

The generator reads either the interim front-matter set files (`docs/decisions/*.md`) or an N-Triples export (`docs/decisions/<ns>.nt`), both arriving as `AdditionalFiles` with `DdLedger` metadata. The read surface is `docs/rules/ledger-input.md`; change it only by issue, in step with the ledger's export shape, and keep the previous shape readable for one release when it changes. Unaccepted tip → `[Obsolete(error: false)]`; revoked without successor → `[Obsolete(error: true)]`; supersession carries the key and emits nothing.

## CI and release

- `ci.yml` calls `build.yml` on every branch: build and test on ubuntu and windows, pack, the samples job against the packed nupkgs (`check-violations.sh`), the report tool on the samples, `dotnet format --verify-no-changes`. The `ci` status is the gathering job; a ruleset requires it with no bypass.
- Versions come from tags through MinVer. A `v0.x.y-preview.N` tag is a request to publish, not a publish: `publish.yml` re-runs the whole check set and pushes the packages that run built, after the `nuget` environment approval (`release-process`: `PublishedBytesAreTestedBytes`, `TagPublishesOnlyThroughGate`).
- Release steps: changelog section `[0.x.y-preview.N] - YYYY-MM-DD` with every `[Unreleased]` entry moved under it and the link references updated; merge; tag the merge commit only after `ci` is green on it; watch the publish run; report run ids and nuget.org indexing for both packages. Tags use the `v` prefix; never create one without it.
- Commits: signed, `Signed-off-by` (DCO), one issue per commit, message names the `<Set>.<Key>` the change implements. Trunk-based, linear history, rebase not merge.

## Sessions

An agent session works from a prompt with ordered steps, each landing as one green PR. Rulings in the prompt are closed; if a step needs a decision the prompt does not cover, write the question and the options into the close-out and continue with the next independent step. Close-outs go in `docs/sessions/YYYY-MM-<name>.md`: PRs, ids taken, test counts before and after, questions, and anything deferred. Never weaken a gate to make a test pass; change the fixture or the test and say which. Upstream issues for other repositories are listed in the close-out, not filed, unless the prompt says to file them.
