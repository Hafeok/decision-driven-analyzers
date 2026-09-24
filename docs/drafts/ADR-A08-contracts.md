# ADR-A08: Contract interfaces are declared decisions (DD0009–DD0012)

**Status:** Proposed
**Date:** 2026-09-22
**Deciders:** Emil

> **Amended 2026-09-24.** The first consumer was named throughout this draft; this
> repository names no consumer (`CLAUDE.md`). Sentences that were about this package now
> say "the first consumer", and sentences that were only about that consumer's own code
> are deleted. Where this narrative and `docs/decisions/contracts.md` disagree, the
> decision file governs.

## Context

Packages talk through small contracts defined over a handful of the consumer's own model types. The initial proposal was a member-count threshold as an ISP heuristic; discussion concluded that member count measures the wrong thing (`IEnumerator<T>` has three members and one ISP violation; a four-member streaming parser sink has none), and that what is wanted is: every contract is a documented decision, every type a contract exposes is from an allowed vocabulary, and the difference between a data parameter and a collaborator parameter is explicit.

## Decision

Applies to public interfaces, public abstract classes and public delegates in any project with `ArchLayer` declared (i.e. all family projects; hosts and tests excluded).

- **DD0009 Contract declaration, tier 1.** Every such type carries `[Contract(typeof(<Set>.<Key>), Role = "...")]` citing a ledger decision (ADR-A07). No member-count threshold: a public interface is a decision regardless of size.
- **DD0010 Contract vocabulary, tier 1.** Every type appearing in a `[Contract]` member signature (parameters, return types, generic arguments, event types) must come from: the BCL, the current assembly's `[DomainModel]` namespaces, an assembly listed in `ArchContractTypeAssemblies`, or be itself `[Contract]`-marked. Anything else is an undecided dependency and fails until an ADR adds the assembly or marks the type.
- **DD0011 Data versus collaborator, tier 1.** A parameter on a `[Contract]` member must be a model type (from the allowed vocabulary), a delegate, a `ReadOnlySpan<T>`/`Memory<T>`/`CancellationToken`, an enum, a generic type parameter, or a `[Contract]`-marked type. An interface or abstract class parameter that is not `[Contract]`-marked is an error: collaborators arrive through constructors or through declared contracts, never as ad-hoc service parameters. `object` parameters are errors.
- **DD0012 Unhonoured contract member, tier 1.** `throw new NotSupportedException(...)` (or a method whose only statement is such a throw) in an interface implementation or in an override is an error: the type is claiming a member it does not honour, which is the concrete symptom of an interface sized for the wrong client. Fix the interface, cite an ADR with `[DesignDecision]`, or don't implement it.

Interface member count and implementer/caller divergence go to the tier-3 report (ADR-A12) as the ISP signal.

## Alternatives considered

- `[Contract]` required only above N members (N = 1, 2, 6 were each discussed). Rejected: at low N the attribute is on everything and the rule is really "all contracts are declared", which is better stated as such; at high N it misses the interfaces that matter.
- Per-parameter attributes declaring why each input exists. Rejected: with ADR-A09 in force the parameter type already is the declaration; an attribute would duplicate the type and drift from it. DD0010 and DD0011 capture what the type alone does not say (which package, data or collaborator).

## Consequences

- Every contract in an adopting repository is tied to a decision; the decision index becomes the contract index.
- Adopting this is a mechanical pass over existing code: every contract that already has a decision behind it needs a `[Contract]` line citing it.
- Adding a fourth vocabulary assembly is a visible `Directory.Build.props` change plus an ADR.
