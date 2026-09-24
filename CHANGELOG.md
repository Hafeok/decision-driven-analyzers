# Changelog

All notable changes to this project are documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

Rule changes are listed by diagnostic id. Adding a rule, raising a rule's default
severity, and promoting a rule from tier 2 to tier 1 are all breaking changes for a
consumer that builds with warnings as errors, and are recorded as such.

## [Unreleased]

The first prerelease. Nothing has been published before this, so everything is new and there is
nothing to deprecate.

### Added

- **The decision ledger source generator.** Reads the decisions a repository has filed and emits
  one static class per set with one nested type per decision, so a citation is a type reference the
  compiler checks. Emits the marker attributes `Contract`, `DomainModel`, `HotPath`,
  `DesignDecision` and `ArchLayer`, and the closed `ExceptionScope` enum, into each consuming
  compilation. A decision with no unrevoked acceptance of its tip version is obsolete as a warning;
  a revoked decision with no successor is obsolete as an error; supersession is neither.
- **The interim decision format.** A markdown set file with YAML front matter carrying `set`,
  `namespace`, and decisions with `key`, `statement`, `accepted-by`, `accepted-at` and `revoked-at`.
  This is the form used until `decision-cli` can export the ledger, and it is deleted once it can.
  The N-Triples reader for that export is implemented and has no producer yet.
- **`DD0001`** - a reference within a family points strictly downward, read from the referenced
  assembly's metadata so package references are checked like project references.
- **`DD0002`** - every `InternalsVisibleTo` target is a test assembly.
- **`DD0003`** - services are resolved only in the composition root.
- **`DD0004`** - no mutable static state, including the static registry.
- **`DD0005`** - no grab-bag name on an assembly or a namespace.
- **`DD0006`** - public types live under the assembly's root namespace.
- **`DD0007`** - a cited decision is a type the generator emitted from the ledger, not a
  hand-written one of the same shape, and `Role` and `Scope` are present where they are required.
- **`DD0008`** - no `#pragma warning disable`, `[SuppressMessage]` or `.editorconfig` severity
  below a rule's declared tier, for any id in the `DD`, `DDBUILD` or `DDGEN` families. The rule is
  not configurable, so it cannot be silenced by the mechanisms it reports. A product package's own
  prefix is added with `dd_rule_id_prefixes` in `.editorconfig`.
- **`DD0009`** - every public interface, abstract class and delegate in a layered project carries
  `[Contract]` citing a decision. No member-count threshold.
- **`DD0010`** - types on a contract signature come from the BCL, a `[DomainModel]` namespace of the
  current assembly, an assembly named in `ArchContractTypeAssemblies`, or are themselves
  `[Contract]`-marked. Generic arguments and array elements are checked like anything else.
- **`DD0011`** - a contract parameter is data: an interface or abstract-class parameter that no
  decision declares a contract is an error, as is `object`. Delegates, enums, spans,
  `CancellationToken`, type parameters and the framework's own interfaces are data.
- **`DD0012`** - `NotSupportedException` thrown anywhere inside a member that implements or
  overrides one. Contract rules are scoped to projects with `ArchLayer` declared, excluding
  composition roots and `*.Tests` assemblies.
- **The code-fixes assembly.** `DecisionDriven.Analyzers.CodeFixes`, packed alongside the analyzers
  in `analyzers/dotnet/cs`: the documented-exception placeholder for every DD rule, which does not
  compile by design, and the design-change fix for `DD0002`. Apply them from a terminal with
  `dotnet format analyzers --diagnostics <id>`.
- **`DDBUILD0001`** - the Roslyn pin matches the floor the analyzers declare, so a dependency bump
  cannot quietly drop support for the oldest SDK in the band.
- **`DDBUILD0002`** - no package is produced without its code-fixes assembly.
- **`DDGEN0001`-`DDGEN0004`** - the decision input is well formed: no duplicate key in a namespace,
  no key that is not an identifier, no key changed between versions, no unparseable export line.
- `DecisionDriven.Report`, a .NET tool, reporting its version. The whole-graph metrics it exists for
  are not implemented yet.

### Notes for consumers

Every decision in this repository's own `docs/decisions/` is unaccepted, which is the mechanism
working rather than an oversight: citing one produces `CS0618` on every citation, so they are usable
on a branch and will not ship under `TreatWarningsAsErrors`.

[Unreleased]: https://github.com/Hafeok/decision-driven-analyzers/commits/main
