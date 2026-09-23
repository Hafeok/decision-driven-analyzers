# Governance

## Who decides

This project has a single maintainer: [@Hafeok](https://github.com/Hafeok). The
maintainer merges pull requests, cuts releases, and is the only person who can accept a
decision.

This is deliberately a small structure for a small project. If the project grows enough
that one person is a bottleneck, that change is itself a decision and is recorded as one.

## Accepting a decision is a maintainer act

Every rule in this repository traces to a decision in `docs/decisions/`. A decision file
carries a set id, a namespace and a list of keys, each with a one-line statement. A
decision with no `accepted-by` is **unaccepted**: it is proposed, it may be cited, and
the generated type is marked obsolete without error so that citing it is visible but not
fatal.

Adding `accepted-by` (and its required `accepted-at`) is an act of the maintainer, not a
side effect of merging code. Concretely:

- A contributor may add a decision key with a statement. A contributor may never add
  `accepted-by`, including for a decision they wrote themselves.
- A pull request that adds `accepted-by` to any file under `docs/decisions/` is not
  merged. Acceptance lands as a separate commit made by the maintainer.
- Revocation (`revoked-at`) is the same kind of act, in the same direction: only the
  maintainer.

The interim front-matter form in `docs/decisions/` stands in for the decision ledger
until the ledger's N-Triples export exists (`decisions-as-types`). When that export
arrives, acceptance moves into the ledger and these files become generated output. The
rule that acceptance is a human act does not change with the format.

## Releases

Versions come from git tags. A `v*` tag produces a release version; every other build of
the trunk produces a prerelease version. Tagging is a maintainer act. Publication runs
from a tag through GitHub Actions with NuGet trusted publishing, so no API key is held as
a repository secret.

## Branch rulesets on `main`

`main` is protected by two rulesets rather than one. They are separate because they have
different bypass lists, and a single ruleset can only have one:

**`ci`** — requires the `ci` status check to pass, with no bypass for anybody. The check
is the gathering job of the CI workflow, not a single matrix leg, so adding a job to CI
does not mean editing the ruleset. Nobody merges past a red build, the maintainer
included: a bypass here would make the check advisory, and an advisory check is not a
check.

**signed commits** — requires commits to be signed, with the Claude GitHub App on the
bypass list. The app is there because an agent session's commits are signed by a key the
account registers, and that arrangement is easier to change than a rule; the bypass keeps
a key rotation from blocking merges. Every other author signs.

Neither ruleset requires a pull request or linear history at the ruleset level. What
protects `main` is that no commit arrives on it without a green `ci` attached to that
exact commit.

That phrasing is load-bearing. A required status check is evaluated against the commit
being pushed, and a commit that has never been built carries no status, so a direct push
of a fresh commit is refused — the check is required and has nothing to report. CI
therefore runs on every branch, which makes the direct-push route possible:

```
git push origin HEAD:refs/heads/my-work    # ci runs, goes green on this SHA
git push origin HEAD:main                  # same SHA, status already attached
```

A pull request reaches the same place by the same rule. Neither is privileged; the
branch simply never receives a commit nobody built.

The bootstrap session left a `.github/rulesets/main.json` describing a single ruleset. It
is deleted rather than kept: it was never applied, it is not the shape above, and a file
whose only use is to be fed to `gh api` is a trap once it disagrees with the branch.

To see what is actually on the branch rather than what this file says:

```
gh api /repos/Hafeok/decision-driven-analyzers/rulesets
```

Changing a ruleset is a maintainer act, and so is adding one.
