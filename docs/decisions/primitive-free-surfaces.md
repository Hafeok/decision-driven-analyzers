---
set: primitive-free-surfaces
namespace: ddd-analyzers
source-draft: ADR-A09
decisions:
  - key: NoNakedPrimitivesOnModelAndContract
    statement: "DD0013: string, numerics, Guid, date/time types and object are banned on public DomainModel and Contract surfaces; bool return, spans, CancellationToken, enums and type parameters are not"
  - key: BoundaryMembersExempt
    statement: "Parse/TryParse/Format/TryFormat, the span-parsable and formattable interfaces, wrapper factories and HotPath members are exempt as the primitive boundary"
  - key: WrappersAreReadonlyStructs
    statement: "DD0014: a DomainModel type wrapping one primitive is a readonly record struct or readonly struct with value equality"
  - key: NoImplicitPrimitiveConversions
    statement: "DD0015: no implicit operator to or from a banned primitive on a DomainModel type"
  - key: FlagArgumentsWarning
    statement: "DD0016 (tier 2): a bool parameter on a contract or public model member is a warning; prefer an enum"
---

Interim set file (decisions-as-types, InterimFrontMatterUntilExport). No `accepted-by`: every decision here is unaccepted until a holder with accept-decision accepts it in the ledger. Narrative: docs/drafts/ADR-A09-*.md.
