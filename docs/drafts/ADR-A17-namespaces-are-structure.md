# ADR-A17: Namespaces are structure (DD0005 v2, DD0022, DD0023, report)

**Status:** Proposed
**Date:** 2026-10-28
**Deciders:** Emil

Where this narrative and the decision files disagree, the decision file governs. The keys this
narrative is behind live in the sets they belong to: `NamesAndNamespaces.BannedGrabBagNames`
(second version), `NamesAndNamespaces.NamespaceMatchesFolder`,
`NamesAndNamespaces.NamespaceDepthNotRequired`,
`StableDependencyRules.NamespacesAcyclicWithinAssembly` and
`WholeGraphReport.NamespaceShapeInReport`.

## Context

The rules so far say that a namespace is not a grab bag (DD0005, ADR-A06) and that it sits under
the assembly name (DD0006). They say nothing about whether a file sits where its namespace says it
does, whether the namespaces inside one assembly depend on each other in one direction, or how
large a namespace has grown. A consumer can pass every current rule with one flat namespace per
assembly that hides several concepts, or with a tree of namespaces whose files are scattered and
whose dependencies run in circles.

The stable-dependencies principle (ADR-A04) is enforced between assemblies: a project references
only lower layers, and DD0001 checks it. Inside an assembly the same principle has had no check at
all. Two namespaces that reference each other are one namespace with two names; a file whose
namespace does not match its folder is a type that is found by search and never by browsing.

None of this requires depth. A small package with one namespace is correctly flat, and a rule that
demanded a second namespace would create structure for the rule's sake. What can be required is
that any structure that exists is real: files match it, and its dependencies form a directed
acyclic graph.

## Decision

Four changes, none of which requires a minimum number of namespaces.

- **Depth is not required** (`NamespaceDepthNotRequired`). No rule demands a minimum number of
  namespaces or a minimum depth; a single-namespace assembly conforms. Recorded as its own
  decision, so the neutrality is cited rather than implied.

- **DD0005, second version** (`BannedGrabBagNames`). The default list gains the plural kind names
  `Interfaces`, `Models`, `Dtos`, `Enums`, `Exceptions` and `Constants`. A namespace named for the
  kind of type in it says nothing about what those types are for, which is the grab-bag problem
  under another name. Singular `Model` is not banned: it names a concept boundary, and is what
  `[DomainModel]` prefixes are commonly keyed on. `Services` is not added. The replace-not-extend
  semantics of `dd_banned_names` are unchanged.

- **DD0022, tier 1** (`NamespaceMatchesFolder`). Every namespace declaration in a source file names
  the assembly's root namespace (`RootNamespace` if set, otherwise the assembly name) followed by
  the file's folder path relative to the project directory, one segment per folder, compared
  ordinally. A type in the global namespace is reported the same way. Files outside the project
  directory (linked files) and generated code are not checked. A folder name that is not a valid
  C# identifier is itself the finding. The rule is silent when the project directory is not known.
  The exception path is `[DesignDecision(..., Scope = ExceptionScope.Migration)]` on every type
  declared directly in the offending namespace declaration. The code fix changes the declaration to
  the expected name; moving the file is not a code fix.

- **DD0023, tier 1** (`NamespacesAcyclicWithinAssembly`). In a compilation with `ArchLayer` set,
  type references between the namespaces of that compilation form a directed acyclic graph. A
  reference is any type symbol bound in a declaration: base types and interfaces, attributes,
  member signatures, generic arguments and constraints, and anything bound in bodies and
  initialisers. A `using` directive alone is not a reference. A nested type belongs to the
  namespace of its outermost containing type, and parent and child namespaces are distinct nodes.
  Every reference that lies on a cycle is an error at its first location, and the message names
  both namespaces and one path back. Like DD0020, the rule applies to every compilation with
  `ArchLayer` set and has no off switch: an `.editorconfig` switch on a tier-1 rule is a
  suppression path. A consumer adopts it by upgrading the package when its namespaces are acyclic.
  `[DesignDecision(..., Scope = ExceptionScope.Migration)]` on a type removes that type's outgoing
  edges.

- **Namespace shape in the report** (`NamespaceShapeInReport`). Per assembly, `DecisionDriven.Report`
  lists the namespace count and, per namespace, its public and total type counts, sorted by total,
  with the largest namespace called out. Non-gating under
  `WholeGraphReport.NothingGatesUntilBaselineDecision`. A consumer that wants a cap writes its own
  decision naming the threshold and the baseline.

## Alternatives considered

- **IDE0130 for the folder rule.** The IDE has a rule that a namespace matches its folder, and it
  would be an off-the-shelf analyzer in the sense of `BuildTimeDependencies.OffTheShelfBeforeOwnRule`.
  Rejected. IDE0130 only runs in the build with `EnforceCodeStyleInBuild=true`; the package cannot
  set that for a consumer without switching on every IDE rule at the consumer's configured
  severities, and this repository itself sets it to `false`. An IDE rule's severity is also
  downgradable in `.editorconfig`, which is a suppression path DD0008 cannot see. The rule is
  therefore DD0022, tier 1, with DD0008 guarding its severity like every other DD rule.
- **A tier-2 namespace size warning.** A namespace with more than *n* types is a grab bag more
  often than not. Rejected under `NamesAndNamespaces.CohesionMetricsAreReport`: size metrics measure
  size, not responsibility, and ADR-A06 rejected member-count thresholds as an SRP proxy for the
  same reason. The count goes to the report, where a consumer that wants a cap can decide one
  against a baseline.
- **Requiring depth.** A rule that an assembly above some size has more than one namespace, or
  that a namespace has no more than some share of the assembly's types. Rejected because small
  packages are correctly flat, and because any such threshold is the size metric above with a
  different name.
- **Making DD0023 opt-in.** A `dd_namespace_dag` switch would let a consumer adopt the package
  without the rule. Rejected for the reason DD0020 has no switch: a tier-1 rule that can be turned
  off in `.editorconfig` has a suppression path DD0008 cannot see. The adoption path is the
  version: a consumer upgrades when its namespaces are acyclic, and the release notes say so.

## Consequences

- DD0023 may make an existing consumer red on upgrade: two namespaces that reference each other
  compiled before and are an error now. The release notes state this. The fix is to move the type
  that closes the cycle, or to merge the two namespaces, which is what the cycle was saying.
- Moving types between namespaces is a breaking change for a published package, so a consumer
  should restructure before its 1.0. This repository restructures its own samples and, where
  DD0023 finds a cycle in `src/`, its own code.
- DD0022 makes a file's location part of its contract with the reader. A consumer whose folders do
  not match its namespaces either moves the files or changes the declarations; the code fix does
  the second, and the first is a rename the IDE does better than a code action would.
- The six new banned names make DD0005 report namespaces it accepted before. A consumer whose
  `dd_banned_names` is set is unaffected, because the setting replaces the list.
- One more report section, non-gating, and one more decision for a consumer to write if it wants a
  size cap.
