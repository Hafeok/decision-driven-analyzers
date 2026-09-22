---
set: static-state
namespace: ddd-analyzers
source-draft: ADR-A05
decisions:
  - key: NoMutableStaticState
    statement: "DD0004: non-readonly statics, readonly statics of mutable types, static registries, ThreadStatic and AsyncLocal statics are errors"
  - key: PoolsExemptByDesignDecision
    statement: "Pools and interning caches are allowed only when marked DesignDecision with Scope Pool citing the decision that introduced them"
---

Interim set file (decisions-as-types, InterimFrontMatterUntilExport). No `accepted-by`: every decision here is unaccepted until a holder with accept-decision accepts it in the ledger. Narrative: docs/drafts/ADR-A05-*.md.
