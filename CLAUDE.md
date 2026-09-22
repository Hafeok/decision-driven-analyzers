# CLAUDE.md — DecisionDriven.Analyzers

Read `docs/drafts/` and `docs/decisions/` before changing anything. Every rule, attribute and tool in this repository is a decision in `docs/decisions/`; code cites decisions with `[Contract(typeof(<Set>.<Key>))]` and `[DesignDecision(typeof(<Set>.<Key>), Scope = …)]`. This repository is its own first consumer: the analyzers run on the analyzers.

Conventions
- Current LTS .NET SDK. Analyzer and generator projects target netstandard2.0 with no dependencies beyond the pinned Roslyn packages. The report tool targets the LTS TFM.
- No new rule without: analyzer, violating and conforming sample tests, exact-message test, doc page `docs/rules/DDnnnn.md` naming tier and principle, and a decision in `docs/decisions/`.
- Diagnostic messages follow the diagnostic-messages set: finding, `Decide:` with both paths, guard sentence.
- Never add `accepted-by` to a decision. Acceptance is a human act in the ledger.
- Never leave a `typeof(____.____)` placeholder in the tree at the end of a session.
- Never use `#pragma warning disable` or `[SuppressMessage]` for DD rules; that is DD0008.
- Commits: signed, one issue per commit, message states the set/key the change implements.
- Nothing in this repository mentions Varve or any other consumer.
