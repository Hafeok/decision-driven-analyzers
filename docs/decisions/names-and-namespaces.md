---
set: names-and-namespaces
namespace: ddd-analyzers
source-draft: ADR-A06
decisions:
  - key: BannedGrabBagNames
    statement: "DD0005: no assembly, root namespace or namespace segment named Common, Core, Utils, Utilities, Helpers, Abstractions, Shared, Misc, public Internal, Extensions as a namespace, Interfaces, Models, Dtos, Enums, Exceptions, or Constants. dd_banned_names in .editorconfig replaces the list rather than extending it, so a consumer narrowing the list does so visibly"
  - key: RootNamespaceEqualsAssemblyName
    statement: "DD0006: every public type lives under a root namespace equal to the assembly name"
  - key: CohesionMetricsAreReport
    statement: "LCOM-family and size metrics are never a gate; they go to the tier-3 report"
  - key: NamespaceMatchesFolder
    statement: "DD0022: every namespace declaration in a source file names the assembly's root namespace followed by the file's folder path relative to the project directory, one segment per folder, compared ordinally; a type in the global namespace is reported the same way; files outside the project directory and generated code are not checked; a folder name that is not a valid C# identifier is itself the finding"
  - key: NamespaceDepthNotRequired
    statement: "No rule requires a minimum number of namespaces or a minimum namespace depth; a single-namespace assembly conforms"
---

Interim set file (decisions-as-types, InterimFrontMatterUntilExport). No `accepted-by`: every decision here is unaccepted until a holder with accept-decision accepts it in the ledger. Narrative: docs/drafts/ADR-A06-*.md; NamespaceMatchesFolder, NamespaceDepthNotRequired and the second version of BannedGrabBagNames: docs/drafts/ADR-A17-*.md.
