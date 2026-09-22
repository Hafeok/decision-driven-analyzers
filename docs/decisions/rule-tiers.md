---
set: rule-tiers
namespace: ddd-analyzers
source-draft: ADR-A01
decisions:
  - key: RuleIsAnalyzer
    statement: "A rule exists only as a Roslyn analyzer with tests and a doc page; the analyzer is the deliverable"
  - key: ThreeTiers
    statement: "Every rule is classified at agreement time as tier 1 (error, mechanical, single compilation), tier 2 (warning, heuristic with a written false-positive story) or tier 3 (CI report, whole graph)"
  - key: Tier2PromotionBySupersession
    statement: "A tier-2 rule becomes tier 1 only by a superseding decision after running on a real codebase without false positives"
  - key: Tier3NeverGatesWithoutBaseline
    statement: "A tier-3 metric gates only by a decision that states the threshold and the baseline it was measured against"
  - key: RuleDeliverableOrder
    statement: "Deliverable order for a rule: analyzer, tests (one violating and one conforming sample per diagnostic), doc page with tier and principle, motivating decision"
---

Interim set file (decisions-as-types, InterimFrontMatterUntilExport). No `accepted-by`: every decision here is unaccepted until a holder with accept-decision accepts it in the ledger. Narrative: docs/drafts/ADR-A01-*.md.
