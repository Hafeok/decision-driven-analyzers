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

## Branch ruleset on `main`

`main` is protected by a branch ruleset with these rules:

| Rule | Setting |
| --- | --- |
| Require a pull request before merging | on, 1 approving review, stale approvals dismissed |
| Require status checks to pass | on, required check `ci`, strict (branch must be up to date) |
| Require signed commits | on |
| Require linear history | on |
| Block force pushes | on |
| Restrict deletions | on |

The maintainer may bypass the ruleset. That bypass exists for one named case — merging a
pull request produced in a cloud agent session whose commits GitHub cannot verify — and
is noted in `CONTRIBUTING.md` so that an unverified commit on `main` is explainable
rather than mysterious. It is not the usual case: a session with a signing key registered
on the account produces commits GitHub verifies, as the bootstrap commits here did.

The ruleset is applied with the GitHub API. The exact call that creates it is kept in
`.github/rulesets/main.json`, so that the protection on `main` is reviewable as a file
rather than only as a screen in the repository settings:

```
gh api --method POST /repos/Hafeok/decision-driven-analyzers/rulesets \
  --input .github/rulesets/main.json
```

The ruleset is **not applied yet**. It was written in a cloud agent session whose GitHub
token is read-only for repository settings, so the call above was refused and has to be
run by the maintainer. Until it is, `main` carries no protection at all — the file
describes the intent, not the state.

Apply it only once `main` exists and is the repository's default branch. The ruleset
targets `~DEFAULT_BRANCH` rather than a branch by name, so applying it earlier would
protect whichever branch happens to be the default at the time.

To check what is actually on the branch:

```
gh api /repos/Hafeok/decision-driven-analyzers/rulesets
```

Changing the ruleset is a maintainer act. Change the file and re-apply it with
`--method PUT` on `/rulesets/{id}`, so that what is in the repository and what is on the
branch stay the same thing.
