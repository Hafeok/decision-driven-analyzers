# Contributing

Thank you for considering a contribution. This repository has a narrow shape on purpose:
every rule it ships exists because a decision says so, and nothing lands without that
decision being visible.

## Certificate of origin: DCO, not a CLA

Contributions are accepted under the [Developer Certificate of Origin 1.1](https://developercertificate.org/).
There is no contributor licence agreement to sign. You certify the DCO by signing off
each commit:

```
git commit -s -m "..."
```

which appends the trailer

```
Signed-off-by: Your Name <your.email@example.com>
```

The name and address must be ones you can be reached at. Commits without a
`Signed-off-by` trailer are not merged.

## Trunk-based development

`main` is the trunk and is always releasable. There are no long-lived branches.

- Branch from `main`, keep the branch short-lived, open a pull request, merge, delete.
- History on `main` is linear. Rebase your branch on `main` rather than merging `main`
  into it.
- A pull request that sits long enough to need a second rebase is too big; split it.

## One issue per commit

Every commit references exactly one issue, and every commit that implements a decision
names the decision it implements as `<Set>.<Key>` — the set id from the front matter of
the file in `docs/decisions/`, and the decision's key.

```
Reject a public type whose name repeats its namespace

Implements names-and-namespaces.NoNamespaceEchoInTypeName. The analyzer
reports DD0101 on the declaration and the message names both paths.

Refs #42
```

Open the issue first. An issue that only exists as a commit message is an issue nobody
can find.

## Signed commits

Commits on `main` are signed. Set up [commit signing](https://docs.github.com/authentication/managing-commit-signature-verification)
with GPG, SSH or S/MIME and turn it on for this repository:

```
git config commit.gpgsign true
```

The maintainer may override the signed-commit requirement when merging pull requests
that were produced in a cloud agent session, until agent commits can be signed with a
key that GitHub verifies. This is a temporary exception recorded here so that an
unsigned commit on `main` is never a surprise; it is not an invitation to send unsigned
commits by hand.

## Adding a rule

A rule is an analyzer, not a document about an analyzer. The deliverable order is fixed
(`rule-tiers.RuleDeliverableOrder`) and a pull request that stops early is incomplete:

1. **Analyzer.** The diagnostic in `src/DecisionDriven.Analyzers/`, with an id in the
   `DD` range, registered in `AnalyzerReleases.Unshipped.md`. Its tier decides its
   default severity: tier 1 is an error, tier 2 is a warning, tier 3 does not ship as a
   diagnostic at all and belongs in `DecisionDriven.Report`.
2. **Tests.** In `tests/DecisionDriven.Analyzers.Tests/`: at least one violating sample
   and one conforming sample per diagnostic, plus a test asserting the exact message
   text. A rule with no conforming sample has not been shown to be a rule rather than a
   ban.
3. **Doc page.** `docs/rules/DDnnnn.md`, following `docs/rules/_template.md`: id, tier,
   principle, motivating decision, configuration, false-positive story, and the
   violating and conforming example. A tier-2 rule without a written false-positive
   story is not finished.
4. **Decision.** The motivating decision in `docs/decisions/`, cited from the analyzer
   and named in the doc page as `<Set>.<Key>`. Add the key if it is not there yet.
   Never add `accepted-by`: acceptance is a human act in the ledger, not something a
   contribution grants itself.

Prefer an off-the-shelf analyzer to a new rule. Where `PublicApiAnalyzers`,
`BannedApiAnalyzers` or the SDK's trimming, AOT and single-file analyzers express the
rule exactly, use them at error severity and write no DD rule
(`build-time-dependencies.OffTheShelfBeforeOwnRule`).

Never silence a DD rule with `#pragma warning disable` or `[SuppressMessage]`. If a rule
is wrong, change the rule or the decision behind it.

## Before you open the pull request

```
dotnet build
dotnet test
dotnet format --verify-no-changes
```

All three must be clean. Warnings are errors here, so a warning is a build failure, not
a note. Fill in the pull request checklist honestly; an unticked box with a sentence
explaining why is more useful than a tick.
