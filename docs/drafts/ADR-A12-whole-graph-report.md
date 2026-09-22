# ADR-A12: Whole-graph metrics are a CI report, not an analyzer

**Status:** Proposed
**Date:** 2026-09-22
**Deciders:** Emil

## Context

An analyzer sees one compilation. Instability (I = Ce / (Ca + Ce)), abstractness, distance from the main sequence, afferent coupling, interface member counts with implementer/caller divergence, cohesion metrics, and "does every cited ADR exist" are properties of the whole solution or the whole repository.

## Decision

A small console tool, `DecisionDriven.Report`, in the generic repository, run as a CI job after build. It reads the built assemblies (via `System.Reflection.Metadata`, no loading) and the repository tree, and emits a Markdown report as a job summary plus a JSON file committed as the baseline.

Reports, none gating initially:

- per package: Ca, Ce, I, A, D, with the layer declared by `[ArchLayer]`, so a package whose I contradicts its layer is visible;
- per `[Contract]` interface: member count, implementer count, and the members each caller uses; divergence is the ISP signal;
- per `[DomainModel]` type: LCOM4;
- the citation projection, emitted as ledger entities in N-Triples so decision-cli ingests it as a read model, never as hashed files: for every citing symbol one `ledger:Citation a prov:Entity` with `ledger:ofDecision`, `ledger:citesVersion <urn:sha256:…>` (the decision's tip at the citation's introducing commit, found by blame), `ledger:symbol` (documentation-comment id, e.g. `T:Varve.Store.IQuadSource`), `ledger:attribute` and `ledger:exceptionScope` (SKOS notations), and `prov:wasGeneratedBy <urn:git:sha1:…>`. This needs `ledger:Commit a prov:Activity` keyed `urn:git:sha1:`/`urn:git:sha256:` in the ledger vocabulary, with change-sets `prov:wasInformedBy` the commit that landed their file (a ledger decision, filed there);
- from that projection, in the Markdown summary: decisions with zero citations (implicit somewhere, or dead; the report decides neither), citations whose `citesVersion` is no longer the tip and carry no waiver (the ledger's own invalidation, applied to code), and per `--since <ref>` the decisions newly cited, which is the record of what an author, human or agent, claimed to be doing when the code was added.

A metric becomes a gate only by an ADR that states the threshold and the baseline it was measured against. ADR existence is no longer the tool's concern: a citation of a missing ADR does not compile (ADR-A07).

## Alternatives considered

- ArchUnitNET tests in a test project. Rejected as the primary mechanism: needs a project referencing every package, which is itself an SDP smell; may still be used inside the tool.
- Not measuring. Rejected: the layer rule guarantees direction, not that the layers are the right ones; instability against declared layer is the check on the layering itself.

## Consequences

- One more project in the generic repository with its own tests.
- A committed baseline JSON that changes in PRs when the shape of the solution changes, reviewed like the public API baseline.
