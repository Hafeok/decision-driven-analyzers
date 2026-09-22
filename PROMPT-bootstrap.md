# Claude Code prompt: bootstrap `hafeok/decision-driven-analyzers`

Run once, in the freshly created, empty repository, with the contents of this bundle's `generic/` folder already copied into the root (`README.md`, `CLAUDE.md`, `PROMPT-session-1.md`, `docs/drafts/`, `docs/decisions/`). This session makes the repository a proper project with an empty solution that builds and a green CI; it writes no analyzer rules. Stop when the definition of done below holds and report; `PROMPT-session-1.md` is the next session.

## Constraints

- Current LTS .NET SDK pinned in `global.json` (`rollForward: latestPatch`). Current C#.
- Nothing here mentions Varve or any consumer. Nothing references a package that ships native assets.
- Licence MPL-2.0. Contributor path uses DCO (`Signed-off-by`), not a CLA.
- Read `CLAUDE.md` and `docs/decisions/` first. Where this prompt and a decision file disagree, the decision file wins; say so in the report.
- Every commit is signed, references an issue, and names the decision set/key it implements where one applies. Create the issues first (`gh issue create`), one per step below, then work them in order.

## Steps

1. **Repository hygiene.** `LICENSE` (MPL-2.0), `README.md` (keep the bundle's README content; add a one-paragraph statement of what the package is and a "status: pre-release, nothing published yet" line), `CONTRIBUTING.md` (DCO, trunk-based, signed commits, one issue per commit, how to add a rule: analyzer → tests → doc page → decision), `SECURITY.md` (private reporting via GitHub security advisories, supported versions table with none yet), `GOVERNANCE.md` (single maintainer for now; states that decision acceptance in `docs/decisions/` is a maintainer act), `CODE_OF_CONDUCT.md` (Contributor Covenant 2.1), `CHANGELOG.md` (Keep a Changelog, `Unreleased` only), `.github/ISSUE_TEMPLATE/` (rule proposal, false positive, bug), `.github/PULL_REQUEST_TEMPLATE.md` (checklist: decision cited, tests, doc page, changelog), `.github/dependabot.yml` (nuget + github-actions, weekly, grouped), `.gitignore`, `.gitattributes` (LF, `*.nt text eol=lf`), `.editorconfig` (C# style, `dotnet_diagnostic` defaults empty for now).
2. **Solution skeleton.** `DecisionDriven.slnx` (or `.sln` if the SDK's slnx support is not stable), `Directory.Build.props` (`TreatWarningsAsErrors`, `Nullable`, `ImplicitUsings` off for analyzer projects, `LangVersion latest`, deterministic builds, `ContinuousIntegrationBuild` on CI, package metadata: authors, MPL-2.0 expression, repository URL, `PackageReadmeFile`), `Directory.Packages.props` with central package management and the packages from `docs/decisions/build-time-dependencies.md` pinned (Roslyn to the lowest version the pinned SDK supports; verify with `dotnet --version` and the SDK's shipped Roslyn version, do not guess), and empty but building projects:
   - `src/DecisionDriven.Analyzers/` — `netstandard2.0`, `IsRoslynComponent`, `EnforceExtendedAnalyzerRules`, packs as analyzer (`analyzers/dotnet/cs`), no lib assets, `DevelopmentDependency=true`. Contains only the assembly-level attributes and an empty `AnalyzerReleases.Shipped.md`/`Unshipped.md` pair.
   - `src/DecisionDriven.Analyzers.Package/` — the `.props`/`.targets` that surface `ArchFamily`, `ArchLayer`, `ArchCompositionRoot`, `ArchContractTypeAssemblies` as `CompilerVisibleProperty` and add `docs/decisions/*.md` and `docs/decisions/*.nt` as `AdditionalFiles` with `DdLedger` metadata. Packaged into the analyzer nupkg under `build/`.
   - `src/DecisionDriven.Report/` — LTS TFM console app, `PackAsTool`, prints its version and exits.
   - `tests/DecisionDriven.Analyzers.Tests/` — xUnit v3 + `Microsoft.CodeAnalysis.CSharp.Analyzer.Testing`, one smoke test that compiles an empty consumer with the analyzer loaded and asserts zero diagnostics.
   - `tests/DecisionDriven.Report.Tests/` — one smoke test.
   - `samples/Consumer/` — three projects `Sample.Layer0`, `Sample.Layer1`, `Sample.Layer2` with `ArchFamily=Sample` and `ArchLayer` 0/1/2, a `Sample.Host` with `ArchCompositionRoot=true`, and `Sample.Layer1.Tests`. They reference the analyzer as a project reference with `OutputItemType="Analyzer" ReferenceOutputAssembly="false"` and build clean. `docs/decisions/` is linked in as `AdditionalFiles` so the generator (session 1) will see it.
   `dotnet build` and `dotnet test` succeed with zero warnings. `dotnet pack` produces `DecisionDriven.Analyzers.<version>.nupkg` and `DecisionDriven.Report.<version>.nupkg`; verify with `unzip -l` that the analyzer nupkg has no `lib/` folder and that the `build/` props are present.
3. **Versioning.** MinVer (or Nerdbank.GitVersioning; pick one, record it as a decision in `docs/decisions/build-time-dependencies.md` as a new key, unaccepted). Prerelease versions from trunk; tags `v*` produce release versions.
4. **CI.** `.github/workflows/ci.yml`: on push to `main` and PRs; matrix ubuntu/windows; restore, build, test, pack, upload the nupkgs as artifacts; a job that builds `samples/Consumer` with `-p:DD_SAMPLE_VIOLATIONS=true` (currently a no-op define, wired now so session 1 only adds the assertions); `dotnet format --verify-no-changes`. `.github/workflows/publish.yml`: on `v*` tags and on manual dispatch for prerelease, NuGet trusted publishing (OIDC, no API key secret), publishes both packages under the `decision-driven-design` NuGet organisation. Do not run publish in this session; confirm the workflow parses with `actionlint` if available.
5. **Rulesets.** Document in `GOVERNANCE.md` the branch ruleset to apply on `main` (require PR, require signed commits, require CI status check `ci`, linear history) and apply it with `gh api` if the token permits; if it does not, print the exact `gh api` command in the report. Note in `CONTRIBUTING.md` that the maintainer may override the signed-commit requirement for cloud-session PRs until agent commits can be signed.
6. **Docs scaffold.** `docs/rules/README.md` explaining the rule page format (id, tier, principle, motivating decision `<Set>.<Key>`, configuration, false-positive story, violating and conforming example) with one template file `docs/rules/_template.md`. No rule pages yet.

## Definition of done

- Clean clone, `dotnet build`, `dotnet test`, `dotnet pack` all succeed with zero warnings on the pinned SDK.
- CI green on `main`.
- Every file in the bundle is committed unchanged except `README.md` additions noted above.
- No `accepted-by` anywhere in `docs/decisions/`.
- The report lists: the pinned SDK and Roslyn versions with how they were verified, the versioning tool chosen and the decision key added for it, whether the ruleset was applied or the command to apply it, and anything in this prompt that could not be done as written and what was done instead.
