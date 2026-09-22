# Claude Code prompt: DecisionDriven.Analyzers (session 1)

Run in the root of the `hafeok/decision-driven-analyzers` repository after dropping in this bundle (`README.md`, `CLAUDE.md`, `docs/drafts/`, `docs/decisions/`). MPL-2.0, MoM stewardship standard, trunk-based, signed commits, every commit tied to an issue.

`docs/drafts/` holds the narrative records; `docs/decisions/` holds the interim set files the generator reads and that code cites. Do not renumber or rename either; do not add `accepted-by` anywhere. Where a draft and a set file disagree, the set file's key list is authoritative for what code may cite; note the disagreement in the session report.

## Session 1: `DecisionDriven.Analyzers`

### Goal

A development-time analyzer package that enforces the generic rules DD0001–DD0019 as specified in ADR-A01, A02, A04–A12, plus the `DecisionDriven.Report` CI tool. Nothing in this repository may mention Varve.

### Constraints

- Current LTS .NET SDK, current C#. Analyzer projects target `netstandard2.0` (Roslyn requirement); the report tool targets the current LTS TFM.
- Roslyn packages per ADR-A03, pinned to the lowest version the current LTS SDK ships. `Microsoft.CodeAnalysis.Analyzers` at error severity on our own analyzers.
- The package is consumed with `PrivateAssets="all"`; verify with a sample consumer project in the repo that the analyzer DLL never appears in the consumer's output.
- No reflection in the analyzers where a Roslyn API exists; performance matters because these run on every keystroke. Register on the narrowest syntax/symbol kinds; use `RegisterCompilationStartAction` to read options once.

### Repository layout

```
src/DecisionDriven.Analyzers/          analyzers + the attribute source generator (ADR-A07)
src/DecisionDriven.Analyzers.Package/  nuspec/build props and targets (CompilerVisibleProperty items, .editorconfig defaults)
src/DecisionDriven.Report/             CI tool (ADR-A12)
tests/DecisionDriven.Analyzers.Tests/  Microsoft.CodeAnalysis.Testing, xUnit v3
tests/DecisionDriven.Report.Tests/
samples/Consumer/                            three-project family (layers 0,1,2) + a host + a test project; must build clean
docs/adr/                                    the ADRs, numbered
docs/rules/DDnnnn.md                         one page per rule: tier, principle, motivating ADR, configuration, false-positive story, examples
```

### Order of work

Each step ends in green tests and a commit. Do not scaffold rules you are not implementing in the same step.

1. Package skeleton, `Directory.Build.props`, the `.targets` file that surfaces `ArchFamily`, `ArchLayer`, `ArchCompositionRoot`, `ArchContractTypeAssemblies` as `CompilerVisibleProperty` and adds `docs/decisions/*.nt` and `docs/decisions/*.md` as `AdditionalFiles` with a `DdLedger` item metadata, and the incremental source generator (ADR-A07). The generator has one internal model (namespace, set, decision, version, acceptance, supersession, revocation) and two readers: a dependency-free N-Triples reader for the ledger export (`ledger:` vocabulary as in the ledger format doc: `ledger:Decision`, `ledger:DecisionVersion` with `ledger:ofDecision`/`ledger:set`/`ledger:key`/`ledger:supersedes`/`prov:wasRevisionOf`, `ledger:Acceptance` with `ledger:signsVersion` and revocation triples), and the interim markdown front-matter reader (`set`, `namespace`, `key`, `statement`, `accepted-by`, `accepted-at`). It emits (a) the attributes `ArchLayerAttribute`, `ContractAttribute`, `DomainModelAttribute`, `HotPathAttribute`, `DesignDecisionAttribute` and the `ExceptionScope` enum as `internal` in namespace `DecisionDriven`, and (b) per ledger namespace, one static class per set with one nested static class per decision (tip version's set decides membership), `[Obsolete(error: false)]` when the tip version has no unrevoked acceptance, `[Obsolete(error: true)]` when revoked without successor, nothing for supersession. Generator diagnostics for malformed input (duplicate key in a namespace, key not matching `^[A-Z][A-Za-z0-9]{0,63}$`, version whose key differs from its parent's, unparseable N-Triples line with line number). Tests: a consumer with `ArchLayer=2` has `[assembly: ArchLayer(2)]` in metadata; the same decision expressed as N-Triples and as front matter produces identical generated source; unaccepted → warning-level obsolete, revoked → error-level, superseded → clean; `typeof(NoSuchSet.NoSuchKey)` fails to compile.
2. DD0001, DD0002, DD0003 (ADR-A04). DD0001 reads `[ArchLayer]` from referenced assemblies' metadata, so the test must use real metadata references built from sample compilations, not source-only tests.
3. DD0004 (ADR-A05), DD0005, DD0006 (ADR-A06).
4. DD0007, DD0008 (ADR-A07). DD0007 verifies the `decision` argument is a generator-emitted decision type and required named arguments are present; DD0008 has no exception path.
5. DD0009–DD0012 (ADR-A08).
6. DD0013–DD0016 (ADR-A09). DD0016 is tier 2: default severity warning.
7. DD0017, DD0018 (ADR-A10); DD0019 (ADR-A11).
8. `DecisionDriven.Report` (ADR-A12): instability/abstractness per assembly from `[ArchLayer]`-marked assemblies, contract member/caller divergence, LCOM4, and the citation projection: for each citing symbol, blame it to its introducing commit (`git log -S` on the attribute line, fallback to first commit touching the symbol's file), resolve the cited decision's tip version at that commit from the ledger export history, and emit `ledger:Citation` entities as N-Triples (`ledger:ofDecision`, `ledger:citesVersion`, `ledger:symbol` doc-comment id, `ledger:attribute`, `ledger:exceptionScope`, `prov:wasGeneratedBy <urn:git:sha1:…>`). Markdown summary: uncited decisions, citations whose version is no longer the tip, and `--since <ref>` newly cited decisions. Report-only; nothing gates. Until the ledger export exists, run the projection over the interim front matter with the same output.
9. `samples/Consumer` exercises every rule with one deliberately violating file per rule behind `#if DD_SAMPLE_VIOLATIONS`, and a CI job that compiles with the define and asserts the expected diagnostic ids appear (this is the end-to-end test that the packaged analyzer, not just the in-memory one, works).
10. Prerelease publish under the decision-driven-design NuGet organisation with trusted publishing, same pattern as Varve's `publish.yml`.

### Definition of done per rule

- Analyzer with a `DiagnosticDescriptor` whose `HelpLinkUri` points at `docs/rules/DDnnnn.md`, category `DecisionDriven`, default severity per tier.
- Message text per ADR-A14: finding, `Decide:` with the design-change path and the documented-exception path, and the guard sentence. A test asserts the exact message for the canonical violating sample.
- Code fixes per ADR-A14: the mechanical design-change fix where one exists, and the `[DesignDecision(typeof(____.____), Scope = ExceptionScope.____)]` placeholder fix (which a test must show does not compile).
- Tests: at least one violating and one conforming sample per diagnostic; for configurable rules, a test per configuration knob; for rules with exemptions (`[DesignDecision]`, boundary members, `*.Tests`), a test per exemption.
- The doc page, with the false-positive story for tier 2.
- The rule listed in the README rule table.

### What to report back

A session report: assigned ADR numbers, the rule table with tier and test counts, anything in the drafts that turned out to be unimplementable as written (say what you did instead and which ADR needs amending), and the prerelease version published.

