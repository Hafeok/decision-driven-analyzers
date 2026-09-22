## What this changes

<!-- One or two sentences. What is different after this merges? -->

Refs #

## Decision

<!--
Name the decision this implements as <Set>.<Key> — the set id from the front matter of
the file in docs/decisions/, and the key. If this change implements no decision (a typo
fix, a CI tweak), say so instead of leaving it blank.
-->

Implements:

## Checklist

- [ ] The commit references exactly one issue and names the decision it implements.
- [ ] Commits are signed and carry a `Signed-off-by` trailer (DCO).
- [ ] A decision in `docs/decisions/` motivates this change, and it is cited above.
- [ ] No `accepted-by` was added to any file in `docs/decisions/`.

For a change that adds or alters a rule:

- [ ] Analyzer, with the diagnostic registered in `AnalyzerReleases.Unshipped.md`.
- [ ] Tests: a violating and a conforming sample per diagnostic, plus an exact-message test.
- [ ] Doc page `docs/rules/DDnnnn.md` naming id, tier, principle and motivating decision,
      with a false-positive story for a tier-2 rule.
- [ ] `CHANGELOG.md` updated under `Unreleased`, by diagnostic id.

Always:

- [ ] `dotnet build`, `dotnet test` and `dotnet format --verify-no-changes` are clean.
- [ ] No `#pragma warning disable` or `[SuppressMessage]` was added for a DD rule.
