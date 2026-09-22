---
set: immutable-model
namespace: ddd-analyzers
source-draft: ADR-A11
decisions:
  - key: DomainModelImmutable
    statement: "DD0019: public DomainModel types have init-only setters, readonly fields, readonly structs and no mutable collection members"
  - key: BuildersAreTheEscapeHatch
    statement: "A sealed *Builder in the same namespace is exempt and may not appear on any contract"
---

Interim set file (decisions-as-types, InterimFrontMatterUntilExport). No `accepted-by`: every decision here is unaccepted until a holder with accept-decision accepts it in the ledger. Narrative: docs/drafts/ADR-A11-*.md.
