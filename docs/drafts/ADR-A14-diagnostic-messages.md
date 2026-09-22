# ADR-A14: Diagnostics ask the decision question

**Status:** Proposed
**Date:** 2026-09-22
**Deciders:** Emil

## Context

The value of adopting the analyzers is that they expose implicit decisions. That value is lost if the diagnostic says only what is wrong: a human or an agent reading `dotnet build` output sees the id and the message, nothing else (descriptions and help links are IDE-only), and the shortest path to green is to add the attribute the rule mentions. The message is the one channel that reaches both readers at the point of violation, so it must carry the review question, not just the finding.

## Decision

Every DD and VARVE diagnostic message follows one template:

```
<what was found> in <where>. Decide: <design-change path> | <documented-exception path>. <guard>
```

- *Design-change path* names the concrete change that removes the violation (introduce a `readonly record struct` for the concept; seal the hierarchy; move the collaborator to the constructor; move the mutable type out of the model namespace).
- *Documented-exception path* names the exact attribute and what its ADR must justify, phrased as a question the ADR has to answer ("which ADR says this contract exposes the raw id?").
- *Guard* is the one sentence that stops laundering: "Do not add the attribute without an ADR that answers this; if the only reason is 'it was already there', take the design change."

Example, DD0013:

```
DD0013: 'IQuadSource.Read' exposes 'long' as parameter 'position' on a contract surface.
Decide: introduce a readonly record struct for the concept 'position' | mark [DesignDecision(typeof(<Set>.<Key>), Scope = ExceptionScope.<Scope>)] where <Key> is the accepted ledger decision that says this contract exposes the raw value.
Do not add the attribute without an ADR that answers this; if the reason is only that the code already looked like this, take the design change.
```

Messages are long by analyzer standards; that is deliberate. The `Title` stays short for the IDE list; the `Description` repeats the message and adds the principle and the ADR link.

Code fixes are offered for the design-change path where it is mechanical (wrap in a new `readonly record struct`, seal a hierarchy, add `init`), and for the exception path only as a fix that inserts `[DesignDecision(typeof(____.____), Scope = ExceptionScope.____)]`. The placeholder does not compile, so the build stays red with a single remaining error that names precisely what is missing: a filed, accepted decision. There is no code fix that makes the build green without either a design change or a real ADR number.

Findings during adoption are reported in two buckets, and the message template is what lets a reader sort them: a violation whose decision question has an answer is a reconstructed decision (file it in the ledger; it is citable in release code once a human accepts it, ADR-A07); one whose only answer is "it was already there" is an accident (take the design change).

## Alternatives considered

- Short messages with the guidance on the help page. Rejected: the help page is not in the build output; agents never see it.
- No exception-path code fix, to prevent laundering. Rejected: the placeholder fix is safer than a hand-typed attribute, because it cannot produce a green build by itself.
- Auto-generating an ADR stub from the code fix. Rejected: a generated ADR is exactly the rationalisation ADR-A01 and the adoption process exist to prevent.

## Consequences

- Message text becomes a reviewed part of each rule, tested like the analyzer (a test asserts the exact message for the canonical violating sample).
- Build logs during adoption read as a decision backlog without post-processing.
