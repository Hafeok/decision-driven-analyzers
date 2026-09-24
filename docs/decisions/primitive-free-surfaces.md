---
set: primitive-free-surfaces
namespace: ddd-analyzers
source-draft: ADR-A09
decisions:
  - key: NoNakedPrimitivesOnModelAndContract
    statement: "DD0013: string, numerics, Guid, date/time types and object are banned on public DomainModel and Contract surfaces; bool return, spans, CancellationToken, enums and type parameters are not"
  - key: BannedPrimitiveListIsAdditiveOnly
    statement: "dd_banned_primitive_types_add adds types to the banned list and can never remove one. A list a consumer could shorten would be a suppression path around DD0013 that leaves no citation, which NoPragmaOrSuppressMessage forbids; removing a banned type is a superseding decision in this repository"
  - key: BoundaryMembersExempt
    statement: "Parse/TryParse/Format/TryFormat, the span-parsable and formattable interfaces, wrapper factories and HotPath members are exempt as the primitive boundary"
  - key: WrapperExposesItsOwnPrimitive
    statement: "A type wrapping a single banned primitive may take and return that primitive on its own members; any other banned type on it is reported. Without this the wrapper DD0013 asks for cannot be constructed or read back"
  - key: WrappersAreReadonlyStructs
    statement: "DD0014: a DomainModel type wrapping one primitive is a readonly record struct or readonly struct with value equality"
  - key: NoImplicitPrimitiveConversions
    statement: "DD0015: no implicit operator to or from a banned primitive on a DomainModel type"
  - key: FlagArgumentsWarning
    statement: "DD0016 (tier 2): a bool parameter on a contract or public model member is a warning; prefer an enum"
---

Interim set file (decisions-as-types, InterimFrontMatterUntilExport). No `accepted-by`: every decision here is unaccepted until a holder with accept-decision accepts it in the ledger. Narrative: docs/drafts/ADR-A09-*.md.
