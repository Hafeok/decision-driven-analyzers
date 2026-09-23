---
set: two-packages
namespace: ddd-analyzers
source-draft: ADR-A02
decisions:
  - key: GenericPackageOwnRepository
    statement: "Generic rules live in DecisionDriven.Analyzers in its own repository; consumers reference a published package, never a project"
  - key: RuleIdPrefixDD
    statement: "Generic rule ids are DD0001 onward; product-specific analyzers use their own prefix and reference the generic package"
  - key: ConfigurationViaMsBuildProperties
    statement: "Consumers configure the rules through CompilerVisibleProperty MSBuild properties (ArchFamily, ArchLayer, ArchCompositionRoot, ArchContractTypeAssemblies) and .editorconfig options; adoption never requires analyzer code changes"
  - key: CodeFixesShipInSeparateAssembly
    statement: "Code fixes ship in their own assembly alongside the analyzers in analyzers/dotnet/cs, because the compiler loads the analyzer assembly and only an IDE or dotnet format loads the fixes"
  - key: DevelopmentTimeOnly
    statement: "The package is consumed with PrivateAssets=all and never appears in a consumer runtime output"
---

Interim set file (decisions-as-types, InterimFrontMatterUntilExport). No `accepted-by`: every decision here is unaccepted until a holder with accept-decision accepts it in the ledger. Narrative: docs/drafts/ADR-A02-*.md.
