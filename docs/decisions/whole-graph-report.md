---
set: whole-graph-report
namespace: ddd-analyzers
source-draft: ADR-A12
decisions:
  - key: WholeGraphMetricsAreCiTool
    statement: "Instability, abstractness, distance, contract divergence and LCOM4 are computed by DecisionDriven.Report over built assemblies, not by an analyzer"
  - key: CitationProjectionAsLedgerEntities
    statement: "The report emits one ledger:Citation per citing symbol as N-Triples with ofDecision, citesVersion, symbol, attribute, exceptionScope and prov:wasGeneratedBy the introducing commit"
  - key: LedgerCommitRequired
    statement: "Citations require ledger:Commit as prov:Activity keyed urn:git:sha1 or urn:git:sha256 in the ledger vocabulary"
  - key: NothingGatesUntilBaselineDecision
    statement: "Every report metric is non-gating until a decision names the threshold and baseline"
  - key: NamespaceShapeInReport
    statement: "The report lists, per assembly, its namespaces with public and total type counts and the largest namespace by type count; non-gating under NothingGatesUntilBaselineDecision"
---

Interim set file (decisions-as-types, InterimFrontMatterUntilExport). No `accepted-by`: every decision here is unaccepted until a holder with accept-decision accepts it in the ledger. Narrative: docs/drafts/ADR-A12-*.md; NamespaceShapeInReport: docs/drafts/ADR-A17-*.md.
