<!--
Copy to docs/rules/DDnnnn.md and fill in. Delete these comments and any section that ends
up empty is a section that is not finished, not one to remove - see README.md.
-->

# DDnnnn: <the rule as an imperative, e.g. "Do not reference a higher layer">

|  |  |
| --- | --- |
| **Id** | `DDnnnn` |
| **Tier** | <1 (error) \| 2 (warning) \| 3 (report)> |
| **Severity** | <error \| warning \| not a diagnostic> |
| **Motivating decision** | `<Set>.<Key>` |
| **Introduced in** | <version, or "unreleased"> |

## Principle

<One sentence of design, not of Roslyn. Someone who thinks this rule is wrong should be
able to point at this sentence and say what they disagree with.>

## What the rule checks

<What the analyzer looks at and what makes it report: the syntax or symbols it examines,
and the condition. Enough that a reader can predict the rule's answer without running it.>

## Configuration

| Setting | Where | Default | Effect |
| --- | --- | --- | --- |
| `Arch…` | MSBuild property | <none> | <what it changes> |
| `dotnet_diagnostic.DDnnnn.severity` | `.editorconfig` | <tier default> | <…> |

<What the rule does when nothing is configured. Usually: nothing, silently. Say so
explicitly - a consumer who has not set these needs to know the rule is not running rather
than that it is running and finding nothing.>

## False-positive story

<Where this rule is wrong, and what to do about it. Required for tier 2; for tier 1, say
that the rule is decidable and what the edge cases are.

The answer is never `#pragma warning disable` or `[SuppressMessage]`: suppressing a DD rule
is DD0008. It is one of: configure the project so the rule's premise is right, change the
code, or change the decision - and if it is the last of those, this section says so.>

## Violating example

```csharp
// <file and the configuration it is built with, if that matters>
```

```text
DDnnnn: <the message, verbatim, exactly as the analyzer emits it and exactly as the
exact-message test asserts it>
Decide: <path A> or <path B>.
<guard sentence>
```

## Conforming example

```csharp
// <the same problem, solved the way the rule wants>
```

<Why this one is not reported, in a sentence, if it is not obvious from the diff.>

## See also

- `docs/decisions/<set>.md` — the decision this enforces
- `docs/drafts/ADR-<nn>-<slug>.md` — the narrative behind it
- <related rule ids>
