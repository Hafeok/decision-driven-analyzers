---
set: immutable-model
namespace: ddd-analyzers
source-draft: ADR-A11
decisions:
  - key: DomainModelImmutable
    statement: "DD0019: public DomainModel types (effectively public: the type and every type containing it) have init-only setters, readonly fields, readonly structs and no mutable collection members. Method bodies too: a non-private instance method on a model class that writes an instance field or property of its own, or invokes a member of a field whose type is mutable by DD0004's test, is reported, directly or through one private helper in the same type. Mutation deeper than one helper is out of scope, and DD0019 does not see it"
  - key: BuildersAreTheEscapeHatch
    statement: "A sealed *Builder in the same namespace is exempt and may not appear on any contract. DD0010 excludes *Builder types from its DomainModel allowance explicitly, because being in a DomainModel namespace would otherwise admit a builder to a contract signature"
---

Interim set file (decisions-as-types, InterimFrontMatterUntilExport). No `accepted-by`: every decision here is unaccepted until a holder with accept-decision accepts it in the ledger. Narrative: docs/drafts/ADR-A11-*.md.
