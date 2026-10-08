---
set: hierarchies
namespace: ddd-analyzers
source-draft: ADR-A10
decisions:
  - key: TypeSwitchOverOpenHierarchyWarning
    statement: "DD0017 (tier 2): a runtime-type switch over a hierarchy that is not closed is a warning; BCL open hierarchies excluded by default"
  - key: ClosedHierarchiesAreSealed
    statement: "A hierarchy is closed by a private or file constructor on the base with sealed leaves; exhaustiveness is then the compiler. A record's copy constructor, which C# requires to be at least protected on a non-sealed record, does not open it"
  - key: NoNotImplementedException
    statement: "DD0018: NotImplementedException anywhere in non-test code is an error; stubs are DesignDecision-marked NotSupportedException tracked by DD0012"
---

Interim set file (decisions-as-types, InterimFrontMatterUntilExport). No `accepted-by`: every decision here is unaccepted until a holder with accept-decision accepts it in the ledger. Narrative: docs/drafts/ADR-A10-*.md.
