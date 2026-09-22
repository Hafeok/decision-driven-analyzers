---
set: diagnostic-messages
namespace: ddd-analyzers
source-draft: ADR-A14
decisions:
  - key: MessageAsksTheDecisionQuestion
    statement: "Every diagnostic message follows finding, Decide: design-change path | documented-exception path, guard sentence"
  - key: PlaceholderFixDoesNotCompile
    statement: "The exception-path code fix inserts a typeof placeholder that does not compile, so no fix reaches green without a filed decision"
  - key: ExactMessageTested
    statement: "Each rule has a test asserting the exact message for the canonical violating sample"
  - key: TwoBucketFindings
    statement: "Adoption findings are sorted by the decision question: reconstructed decisions are filed unaccepted; accidents take the design change"
---

Interim set file (decisions-as-types, InterimFrontMatterUntilExport). No `accepted-by`: every decision here is unaccepted until a holder with accept-decision accepts it in the ledger. Narrative: docs/drafts/ADR-A14-*.md.
