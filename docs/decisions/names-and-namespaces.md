---
set: names-and-namespaces
namespace: ddd-analyzers
source-draft: ADR-A06
decisions:
  - key: BannedGrabBagNames
    statement: "DD0005: no assembly, root namespace or namespace segment named Common, Core, Utils, Utilities, Helpers, Abstractions, Shared, Misc, public Internal, or Extensions as a namespace; list configurable"
  - key: RootNamespaceEqualsAssemblyName
    statement: "DD0006: every public type lives under a root namespace equal to the assembly name"
  - key: CohesionMetricsAreReport
    statement: "LCOM-family and size metrics are never a gate; they go to the tier-3 report"
---

Interim set file (decisions-as-types, InterimFrontMatterUntilExport). No `accepted-by`: every decision here is unaccepted until a holder with accept-decision accepts it in the ledger. Narrative: docs/drafts/ADR-A06-*.md.
