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
