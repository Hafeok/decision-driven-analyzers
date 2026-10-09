# Session close-out: issue clean-up to 0.1.0-preview.8

8 October 2026. Started on `main` at `60b4a68` (PR #86 merged, `v0.1.0-preview.7` tagged, published
by run 37748067746 and indexed on nuget.org for both packages). Ends with `0.1.0-preview.8`.

## Rule id

**#69 took `DD0020`** (`StableDependencyRules.NoDynamicInLayeredProjects`). The next free DD id is
`DD0021`, for the `Discharges` session.

## Test counts

| | Total | Analyzers | Report |
| --- | --- | --- | --- |
| Before (`60b4a68`) | 407 | 386 | 21 |
| After (`c874239`, `v0.1.0-preview.8`) | 471 | 449 | 22 |

The packaged samples check (`check-violations.sh`) reports 21 packaged ids and DDBUILD0001 exactly
once; it reported 20 before DD0020.

## Step 0: bucket A closures

29 issues closed with a comment naming the PR and the test or file that covers it. Every
verification held; none moved to bucket C.

#1, #2, #3, #4, #5, #6 (bootstrap commits); #10, #11, #12 (PR #13); #14, #16, #18 (PR #26 and
earlier); #15 (commit 980412f); #17; #19; #20; #50 (PR #57); #51 (PR #56); #52 (PR #55); #53
(PR #79); #54 (PR #58); #59, #62, #63, #64 (PRs #65-#68); #71 (PR #77); #72 (PR #75); #73 (PR #76);
#74 (PR #78).

## Steps 1 and 2: one PR per issue

| Issue | PR | Decision | Merged as |
| --- | --- | --- | --- |
| #44 | #87 | `TwoPackages.ConfigurationViaMsBuildProperties` | merge commit |
| #45 | #88 | `TwoPackages.DevelopmentTimeOnly` (amended) | merge commit |
| #49 | #89 | `NamesAndNamespaces.BannedGrabBagNames` (amended) | merge commit |
| #80 | #90 | `StaticState.NoMutableStaticState` | merge commit |
| #60 | #91 | `PrimitiveFreeSurfaces.FlagArgumentsWarning` (amended) | merge commit |
| #61 | #94 | `DecisionsAsTypes.AttributesAreSourceGenerated` | merge commit |
| #81 | #95 | `DecisionsAsTypes.NTriplesExportIsGeneratorInput` | rebase |
| #82 | #96 | `DecisionsAsTypes.NTriplesExportIsGeneratorInput` | squash |
| #83 | #97 | `DecisionsAsTypes.RevocationIsItsOwnNode` (new; DDGEN0006) | squash |
| #46 | #98 | `DecisionsAsTypes.DomainModelIncludesSubNamespaces` (new) | squash |
| #48 | #99 | `StableDependencyRules.FamilyProjectDeclaresLayer` (new) | squash |
| #47 | #100 | `ImmutableModel.DomainModelImmutable` (amended) | squash |
| #69 | #101 | `StableDependencyRules.NoDynamicInLayeredProjects` (new), `CallbackLoopholeNeedsNoRule` (amended) | squash |

Every new or amended key is unaccepted; no `accepted-by` was added.

## Step 3: dependabot #70

Merged as a squash (`e5d6305`). Its green CI dated from 28 September, against a `main` that predates
this batch, so its branch was first brought up to `main` with a merge (no rewrite of the bot's
branch) and CI re-ran green on that head (runs 37757513392, 37757516938), including the samples job,
which downloads the packages artifact with the new `actions/download-artifact@v8`.

## Step 4: release

Release PR #102 moved every `[Unreleased]` entry under `[0.1.0-preview.8] - 2026-10-08`, with a lead
paragraph naming what breaks a warnings-as-errors build, and updated the link references. It merged
as `c874239`.

| | Run | Result |
| --- | --- | --- |
| `ci` on `c874239`, before tagging | [37758832045](https://github.com/Hafeok/decision-driven-analyzers/actions/runs/37758832045) | green: build (ubuntu, windows), format, samples, `ci` |
| `publish` started by `v0.1.0-preview.8` | [37763995993](https://github.com/Hafeok/decision-driven-analyzers/actions/runs/37763995993) | green: the full check set on the tagged commit, then `publish`, Push step 10:34:25-10:34:27 UTC after the `nuget` approval |

`v0.1.0-preview.8` points at `c874239`. nuget.org lists `0.1.0-preview.8` for both
`DecisionDriven.Analyzers` and `DecisionDriven.Report` (published 2026-10-08 10:34 UTC; both
flat-container indexes checked).

The tag was pushed by the maintainer. This session's own push of the tag was refused by its egress
policy (HTTP 403 on `refs/tags/*`; branch pushes were allowed), and was not retried or routed
around.

## #84

Left open as ruled, labelled `milestone`. Its body was not edited, so its adopter references are
still there to strip when it next is.

## Found along the way

- **#44.** The `.targets` comment said an empty `DdLedgerDirectory` turns the globbing off. It
  cannot: MSBuild does not tell empty from unset, so empty is the default. The comment and the
  README now say so. A consumer that wants no globbing has no switch for it today; pointing the
  property at a directory that does not exist is the only way.
- **#49.** `docs/rules/DD0005.md` said an empty `dd_banned_names` turns DD0005 off. The analyzer has
  always treated empty as unset and applied the default list. The page was corrected to match the
  code and a test pins it; behaviour is unchanged.
- **#60.** The existing exact-message test pinned `'Read(x, true)'` for a one-parameter member,
  which is the template error the issue describes. It now expects `'Read(true)'`.
- **#61.** The test stand-in `HotPathAttribute` lacked `Constructor` (from #75) as well as
  `Interface`; it now matches the generated one.
- **#48: `dd_` options in `.editorconfig` are silently ignored.** See the issue draft below. Not
  fixed here: no change beyond the ruled items. The maintainer files it.

## Unsigned commit on main

**`cbaed79`** ("NTriplesReader: the set id is the ledger:set IRI's local part", #81 via #95) is the
one unsigned commit this batch put on `main`. Cause: it was merged with GitHub's **rebase** merge.
The branch commit was signed with the session's SSH key, but a rebase merge recreates each commit
with GitHub as committer and does not sign it, so the signature did not survive. GitHub does sign
the commits it creates for merge and squash merges, which is why every later PR was squashed. `main`
was not rewritten to re-sign it.

## Process notes

- **Merge method.** `CLAUDE.md` was rewritten on `main` mid-session (#93) and now asks for linear
  history. #87-#91 and #94 had already merged with merge commits, as earlier PRs (#86) had. #95 was
  rebase-merged; from #96 on, PRs were squash-merged with the PR's own commit message, which is
  linear and signed by GitHub.
- **Samples cases per `RuleTiers.RuleDeliverableOrder` step 6** (also new in #93): #48 got a
  conforming case (`samples/Consumer/.globalconfig`, `Sample.Host` placed at layer 3); #46's case is
  under `Violations/`, because the conforming build cites no decision and so cannot declare a
  `[DomainModel]`. It is falsifiable: with `IncludeSubNamespaces = true` the samples job fails on
  DD0014 and DD0019 reported twice, which a local run confirmed.
- **Ruled behaviour, recorded:** DD0020 gates on `ArchLayer` only, with no composition-root or test
  exemption. DD0019's body check covers ordinary methods only; property accessors (a lazy cache in a
  getter) are not checked, and the rule page says so.

## Issue draft: `dd_` options in `.editorconfig` are silently ignored

To be filed by the maintainer.

> **Title:** `dd_banned_names`, `dd_banned_primitive_types_add` and `dd_require_layer` are ignored in
> `.editorconfig`; only `.globalconfig` reaches them
>
> ### What happens
>
> `docs/rules/DD0005.md` and `docs/rules/DD0013.md` document `dd_banned_names` and
> `dd_banned_primitive_types_add` as `.editorconfig` options. Both analyzers read them only from
> `AnalyzerConfigOptionsProvider.GlobalOptions` (`BannedNameAnalyzer.ReadBannedNames` and
> `BannedPrimitives.Read`, called with `GlobalOptions`). Roslyn fills `GlobalOptions` from global
> analyzer config files (`is_global = true`) and MSBuild-visible properties, never from a section of
> `.editorconfig`, which applies per file. A consumer who follows the docs gets no error, no
> warning, and the default list. `dd_require_layer` (DD0001, 0.1.0-preview.8) reads the same way and
> is documented as `.globalconfig` for that reason.
>
> ### Reproduction
>
> In this repository, at `v0.1.0-preview.8`, building `samples/Consumer/Sample.Layer0`:
>
> | Configuration | Result |
> | --- | --- |
> | none | build succeeds |
> | `samples/Consumer/.editorconfig`: `[*.cs]` `dd_banned_names = Layer0` | build succeeds: option not read |
> | `samples/Consumer/.globalconfig` (`is_global = true`): `dd_banned_names = Layer0` | `DD0005` twice: assembly `Sample.Layer0` and namespace `Sample.Layer0` |
>
> The same with `Sample.Host` and no `ArchLayer`: `dd_require_layer = true` under `[*.cs]` in
> `.editorconfig` builds clean; in `.globalconfig` it reports `DD0001` ("'Sample.Host' is in family
> 'Sample' and declares no layer"). `dd_banned_primitive_types_add` takes the same `GlobalOptions`
> path by code and was not built separately.
>
> The unit tests do not catch it: `RuleHarness.OptionsFor` feeds a dictionary straight into
> `GlobalOptions` and bypasses Roslyn's config handling.
>
> ### Options
>
> 1. **Read them from `.editorconfig` too.** Fall back to the tree options
>    (`AnalyzerConfigOptionsProvider.GetOptions(tree)`) when `GlobalOptions` has no value. A
>    per-file option on a per-compilation question needs a rule for files that disagree (first tree,
>    union, or a diagnostic on disagreement), and the assembly-name checks have no tree of their own.
> 2. **Document them as `.globalconfig` only, and report a diagnostic when one appears in
>    `.editorconfig`.** The rule pages and decision statements name `.globalconfig`; a new
>    diagnostic (next free id in its family) reports a `dd_` option found in a tree's options and not
>    in the global ones, so the silent case becomes a visible one.
>
> Either way the fix is tested through a real config file (`RuleHarness.VerifyThroughAnalyzerConfigAsync`
> or the samples job), and a changed statement is a new version of its key.

## Questions

None blocking. The issue draft above is the maintainer's to file and decide.
