# Session close-out: DD0019 null tests and the published-once memo, 0.1.0-preview.11

27 October 2026. Started on `main` at `724b00c` (`v0.1.0-preview.10`). Ends with `0.1.0-preview.11`.

## Rule id

None taken. Both steps change DD0019. Its descriptor (id, title, message format, description) is
unchanged, and the new findings are new `{0}`/`{1}` arguments, so `AnalyzerReleases.Unshipped.md`
did not change. The next free DD id is still `DD0021`.

## Test counts

| | Total |
| --- | --- |
| Before (`724b00c`) | 541 |
| After step 1 (`3191728`) | 549 |
| After step 2 (`69c393d`) | 580 |

Step 1 added 8 tests to `MemoizedFieldTests`:

- #118's reproduction, as a conforming fact;
- four conforming rows, one per null-test spelling (`is not null`, `is null`, `!= null`, `null ==`);
- three violating rows, where a null test sits beside a real hand-out (returned, stored, passed).
  Each is checked against the full message verbatim.

Without the fix, all 8 fail. The conforming ones report a hand-out, and the violating ones report it
twice.

Step 2 added `PublishedMemoTests`, with 31 tests:

- 9 conforming rows;
- 20 violating rows, each checked verbatim;
- 2 facts: a readonly set is still a plain collection, and the control without the attribute is
  reported as an ordinary write.

The three plain-collection rows in `MemoizedFieldTests` now carry the new design change.

The packaged samples check reports 20 packaged ids once each, DD0019 twice and DDBUILD0001 once.

## PRs

| Issue | PR | Decision | Merged as |
| --- | --- | --- | --- |
| #118 | #120 | `ImmutableModel.MemoizedFieldsAreVerified` (statement unchanged: a null test is a read) | squash `3191728` |
| #119 | #121 | `ImmutableModel.MemoizedFieldsAreVerified` (new version), ADR-A11 narrative amended | squash `69c393d` |
| release | #122 | `ReleaseProcess.TagPublishesOnlyThroughGate` | squash `9d71855` |

The new version of the key is unaccepted. No `accepted-by` was added.

## Release

| | Run | Result |
| --- | --- | --- |
| `ci` on `3191728` (#120 merged) | [40490983173](https://github.com/Hafeok/decision-driven-analyzers/actions/runs/40490983173) | green |
| `ci` on `69c393d` (#121 merged) | [40492188963](https://github.com/Hafeok/decision-driven-analyzers/actions/runs/40492188963) | green |
| `ci` on `9d71855` (#122 merged), before tagging | see the session report | |
| `publish` started by `v0.1.0-preview.11` | see the session report | |

## Rulings made inside the prompt's scope

The prompt's rulings were closed. The points below are the ones it left open. Each one is
implemented as described, and each can be changed by a new version of `MemoizedFieldsAreVerified`.

- **"Element and value types are immutable" includes a dictionary's keys.** A mutable key in a hash
  collection is wrong whatever else holds, and the check errs toward reporting. The shape message
  names which of elements, keys or values is mutable.
- **A readonly `HashSet`/`Dictionary` is still the plain-collection finding.** A readonly field
  cannot be published once, so it is the filled-in-place shape #119 leaves refused. Because of
  that, the plain-collection message's design change now also names the published-once form.
- **The writer must publish a local**, as the ruling says. `ids ??= Build()` and
  `ids ??= new HashSet<long>(xs)` are reported as stores. The helper itself may return either `new …`
  directly or one local holding one.
- **"Fresh" means an object creation** of `HashSet<T>` or `Dictionary<K, V>`, or a private helper
  that returns one. `ToHashSet()` and a collection expression are not fresh.
- **The expression rule covers the build region.** For an inline build, that is the statements from
  the local's declaration to the publishing statement, in the block that declares the local. For a
  helper, it is the helper's whole body. Locals declared outside the region are refused as "computed
  outside the memo", the same as a value memo.
- **"Doesn't escape" counts these as escapes:** a capture by a lambda or local function, a method
  group of the local, and a reassignment of the local. Each is reported with the escape message.
  The message says the local was "let out", which fits a reassignment only loosely.
- **After publication, the local is held to the same rule as the field.** A write through it is
  "a write after publication". Returning or passing it is the hand-out message. "After" is source
  order. A loop that publishes and then adds on its next iteration is not seen.
- **The publication must be a statement.** If `CompareExchange`'s result, or `??=`'s result, is
  used, it hands out the field, and that is reported as a hand-out.
- **`field?.Read()` is a read on a published-once field.** `field?.Add(x)` is a write after
  publication. That is the conditional-access form of the held-collection reads the ruling allows.
- **The samples check reads a count.** `violations.expected` may give a count after an id
  (`DD0019 2`). The check is still exact. Without a count, a second DD0019 sample could not be added,
  and the alternative was to drop the existing setter violation.

## Questions

1. **`?.` on a per-key memo.** `_cache?.GetOrAdd(...)` on a `[Memoized]` `ConcurrentDictionary` is
   still reported as a hand-out. Step 1 covered the four null tests the prompt names, and step 2 added
   `?.` for the published-once form only. The options are (i) to leave it, or (ii) to treat `?.` on a
   per-key memo the way the published-once form treats it.
2. **`field ??= Build()` without a local.** It is as safe as publishing a local that holds the
   helper's result. The options are (i) to keep requiring the local, as ruled, or (ii) to accept a
   direct call to a qualifying helper as the published value.
3. **Questions 1 and 2 from the previous close-out** (a nested type writing an outer memo, and
   `[Memoized]` outside a model class) still stand.

## Found along the way

- The session branch could not be reset onto `main` after each squash merge. As in the last
  session, each later PR merged `main` into the branch, which kept the net diff to the step's own
  change. The squash merges kept `main` linear.

## Deferred

- Nothing from the prompt.
