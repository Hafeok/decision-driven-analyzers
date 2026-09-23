---
set: build-time-dependencies
namespace: ddd-analyzers
source-draft: ADR-A03
decisions:
  - key: RoslynPinnedToLowestSupported
    statement: "Microsoft.CodeAnalysis.CSharp is pinned to the lowest version the current LTS SDK supports"
  - key: OffTheShelfBeforeOwnRule
    statement: "PublicApiAnalyzers, BannedApiAnalyzers and the SDK trimming/AOT/single-file analyzers are used at error severity wherever they express a rule exactly; a DD rule is written only for what they cannot express"
  - key: AnalyzerTestingHarness
    statement: "Analyzer and generator tests use Microsoft.CodeAnalysis.Testing with xUnit v3"
  - key: VersionFromGitTags
    statement: "Package versions are derived from git tags by MinVer: a v* tag is a release version and every other build of the trunk is a prerelease"
  - key: WorkspacesForCodeFixesOnly
    statement: "Microsoft.CodeAnalysis.Workspaces is referenced only by the code-fixes assembly, pinned with the other Roslyn packages, and supplied by the host at run time rather than shipped"
  - key: RoslynPinEnforcedByBuild
    statement: "The Roslyn pin is enforced by an MSBuild check (DDBUILD0001) that fails the build on a bump; Microsoft.CodeAnalysis.CSharp, Microsoft.CodeAnalysis.Analyzers and Microsoft.CodeAnalysis.CSharp.Workspaces move together and only by a deliberate decision, which is why all three are ignored by Dependabot"
---

Interim set file (decisions-as-types, InterimFrontMatterUntilExport). No `accepted-by`: every decision here is unaccepted until a holder with accept-decision accepts it in the ledger. Narrative: docs/drafts/ADR-A03-*.md.
