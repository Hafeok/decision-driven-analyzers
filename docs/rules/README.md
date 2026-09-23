# Rule pages

One page per diagnostic, named for its id: `docs/rules/DD0001.md`. The page is part of the
rule, not documentation about it — a rule is not finished until its page exists
(`RuleTiers.RuleDeliverableOrder`). Start from [`_template.md`](_template.md).

A page exists to answer the question somebody actually has, which is never "what does this
rule check". It is "this fired on my code, now what". So the page has to say what the rule
wants, which decision made it want that, and what the conforming shape looks like.

## The sections

| Section | What goes in it |
| --- | --- |
| **Id** | `DDnnnn`. Generic rules are `DD0001` onward (`TwoPackages.RuleIdPrefixDD`). An id is never reused, even after a rule is removed. |
| **Tier** | 1, 2 or 3, with the severity that follows from it. Fixed when the rule is agreed, not when it is written (`RuleTiers.ThreeTiers`). |
| **Principle** | The design idea in one sentence, in the language of design rather than of Roslyn. Somebody who disagrees with the rule should be able to disagree with this sentence. |
| **Motivating decision** | `<Set>.<Key>` — the generated static class for the set (the PascalCase of the `set:` id in the front matter, so `build-time-dependencies` is `BuildTimeDependencies`) and the decision's key. Exactly the string the analyzer cites, which is a C# type: `typeof(BuildTimeDependencies.RoslynPinnedToLowestSupported)`. A rule with no decision behind it is an opinion, and does not ship. |
| **Configuration** | The MSBuild properties and `.editorconfig` options that change what the rule does, with defaults, and what the rule does when they are unset. "Unset" is a real case and usually means the rule is silent. |
| **False-positive story** | Where this rule is wrong, and what a consumer does about it. Required for tier 2. Suppressing the diagnostic is not an answer — that is DD0008. |
| **Violating example** | The smallest code that is reported, with the diagnostic it produces, verbatim. |
| **Conforming example** | The same problem solved the way the rule wants. Not merely code that is silent: code a reader would want to write. |

## The tiers, and what each owes the page

- **Tier 1** — mechanical and decidable inside one compilation. Ships as an error. The page
  still needs a false-positive section; it is allowed to say that the rule is decidable and
  has none, but it has to say it.
- **Tier 2** — heuristic. Ships as a warning, and the false-positive story is the section
  the page lives or dies on. A tier-2 rule becomes tier 1 only by a superseding decision,
  after it has run on a real codebase without false positives
  (`RuleTiers.Tier2PromotionBySupersession`).
- **Tier 3** — needs the whole graph and so cannot be a diagnostic at all. It lives in
  `DecisionDriven.Report`. The page says which report it appears in, and, if it gates a
  build, names the threshold and the baseline the threshold was measured against
  (`RuleTiers.Tier3NeverGatesWithoutBaseline`).

## Quoting the diagnostic

The violating example quotes the message exactly as the analyzer emits it, because there is
a test asserting that same string. When a message changes, the test and the page change in
the same commit — a page quoting a message that no longer exists is worse than a page with
no example.

Messages follow the `diagnostic-messages` set: the finding, a `Decide:` line giving both
paths, and the guard sentence.
