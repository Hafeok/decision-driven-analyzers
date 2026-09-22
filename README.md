# DecisionDriven.Analyzers — repo bundle

Drop into the empty repository root, run `PROMPT-bootstrap.md` in Claude Code, then `PROMPT-session-1.md`.

- `docs/drafts/` — narrative decision records (ADR-A01…A14 minus A13). Kept as narrative; not what code cites.
- `docs/decisions/` — interim set files, one per draft, in the front-matter form of decisions-as-types. Each decision has a key and a one-line statement and no acceptance. These are what the generator reads and what code cites as `typeof(<Set>.<Key>)` until decision-cli exports the ledger.
- `CLAUDE.md` — repository conventions for agents.
- `PROMPT-session-1.md` — the build prompt.

Interim set file format (read by the generator until the N-Triples export exists):

```yaml
---
set: <kebab-case set id>          # becomes the PascalCase static class name
namespace: <ledger namespace>     # becomes DecisionDriven.Ledger.<namespace>
decisions:
  - key: <PascalCase, ^[A-Z][A-Za-z0-9]{0,63}$>
    statement: "<one line>"
    accepted-by: mailto:<identity>   # optional; absent = unaccepted = Obsolete(error: false)
    accepted-at: <xsd:dateTime>      # required with accepted-by
    revoked-at: <xsd:dateTime>       # optional; present = Obsolete(error: true)
---
```

Set ids: lowercase alphanumerics, dashes, dots (ledger set id rules). Keys unique per namespace across all set files.
