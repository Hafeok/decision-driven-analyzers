# Changelog

All notable changes to this project are documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

Rule changes are listed by diagnostic id. Adding a rule, raising a rule's default
severity, and promoting a rule from tier 2 to tier 1 are all breaking changes for a
consumer that builds with warnings as errors, and are recorded as such.

## [Unreleased]

### Fixed

- **README** did not say what `DdLedgerDirectory` does. A new section, "Where the decisions come
  from", gives the per-project default, the two non-recursive globs onto `DdLedger` metadata, why a
  `README.md` beside the set files is harmless, and the hand-written `AdditionalFiles` form. The
  `.targets` comment no longer says an empty value turns the globbing off; empty is the default
  ([#44](https://github.com/Hafeok/decision-driven-analyzers/issues/44)).
- **README** quick start referenced the package with `PrivateAssets="all"` only, while ADR-A02
  prescribes `IncludeAssets="analyzers;build"` as well. The quick start and the samples now use
  both, and `TwoPackages.DevelopmentTimeOnly` (amended) states the same reference form
  ([#45](https://github.com/Hafeok/decision-driven-analyzers/issues/45)).
- **`DD0005`** decision and rule page disagreed about `dd_banned_names`. The decision said the list
  was "configurable"; the rule page said the option replaces it. `NamesAndNamespaces.BannedGrabBagNames`
  (amended) and ADR-A06 now say it replaces the list rather than extending it, so setting it to add
  one name drops the ten defaults. The rule page also said an empty value turns the rule off, which
  it never did: an empty value leaves the default list in force, and a test now says so
  ([#49](https://github.com/Hafeok/decision-driven-analyzers/issues/49)).
- **`DD0004`** reported a static property with no backing field, such as `=> Names.Where(...)`, as
  though it held a value of its return type, and named a computed sequence a static registry. Only a
  property with storage, an auto-property or one whose accessor uses `field`, is now checked by its
  type; a computed one holds nothing and is not reported. A settable static property is reported as
  before ([#80](https://github.com/Hafeok/decision-driven-analyzers/issues/80)).
- **`DD0016`** reported the constructor of a type wrapping one `bool`, the shape DD0014 asks for, as
  taking a flag. The one-parameter constructor or static factory of a single-`bool` wrapper, taking
  that `bool`, is now exempt, as DD0013 exempts a wrapper's own primitive
  (`PrimitiveFreeSurfaces.FlagArgumentsWarning`, amended). The message also described every call
  site as `Member(x, true)`, including one-parameter members; it now reads the call from the
  member's parameters, `new T(...)` for a constructor
  ([#60](https://github.com/Hafeok/decision-driven-analyzers/issues/60)).
- **`[HotPath]`** could not be applied to an interface (CS0592), so a `[Contract]` interface, the
  contract most likely to be on a hot path, could not be marked as a whole. `HotPathAttribute` now
  has `AttributeTargets.Interface`, and DD0013 exempts the members of a marked interface as it does
  a marked class or struct
  ([#61](https://github.com/Hafeok/decision-driven-analyzers/issues/61)).
- **N-Triples reader** took the object of `ledger:set` as the set id verbatim. The export writes it as
  an IRI, `<urn:ledger-set:sample-set>`, so the generated set class and its `SetId` were named after
  the whole IRI. The set id is now the IRI's local part; a literal is read unchanged. A fixture
  export in the ledger's shape is tested alongside the older hand-written form
  ([#81](https://github.com/Hafeok/decision-driven-analyzers/issues/81)).
- **N-Triples reader** kept one `rdf:type` per node, the last one read. The export types every node
  twice, `ledger:` and `prov:`, and it was read correctly only because code-point order happens to
  put the `prov:` line first; any other order dropped every decision, version and acceptance
  silently. Every type is now kept, and a node is selected by the `ledger:` type among them
  ([#82](https://github.com/Hafeok/decision-driven-analyzers/issues/82)).

## [0.1.0-preview.7] - 2026-10-08

### Fixed

- **`DD0010`** read `ArchContractTypeAssemblies` only up to the first `;`, so with
  `<ArchContractTypeAssemblies>A;B</ArchContractTypeAssemblies>` it reported every type from `B`.
  The compiler reads a visible MSBuild property from a generated analyzer config file, and that
  file's parser takes everything after a `;` as a comment. The package now hands the analyzers the
  list with `,` as its separator; `A;B` is written as before, and every assembly in it is accepted
  (#85).

## [0.1.0-preview.6] - 2026-09-29

### Fixed

- **`DD0004`** reported a `static readonly` field of a type derived from an exempt base, such as
  `UTF8Encoding` or a source-generated `Regex`, because the exemption matched the base's name only.
  A class derived from `Regex`, `Encoding`, `ArrayPool<T>` or `MemoryPool<T>` is now exempt with it
  (#53).
- **`DD0016`** reported the `disposing` parameter of the framework's dispose pattern,
  `protected virtual void Dispose(bool disposing)`, which CA1063 requires on an unsealed disposable
  type. The pattern, or an override of it, on an `IDisposable` type is no longer reported
  (`PrimitiveFreeSurfaces.FlagArgumentsWarning`, amended; #74).
- **`DD0010`** checked the `internal` and `private protected` members of a `[Contract]` class, which
  no consumer outside the assembly can reach. An internal constructor that builds the contract from
  engine state was reported for naming the engine type. It now checks only members reachable from
  outside the assembly: `public`, `protected` and `protected internal` (#71).
- **`DD0017`** reported a switch over a hierarchy declared in a referenced assembly as open when a
  `private protected` or `internal` constructor closed it. A reference assembly carries neither, so
  the base showed no constructor and read as open, and leaves were looked for in the consuming
  compilation instead of the defining one. A referenced base is now closed when no constructor is
  callable from outside its assembly and every type its assembly derives from it, enumerated from
  metadata, is sealed (#73).
- **`[HotPath]` can mark a constructor.** The generated attribute's usage left `Constructor` out,
  so a struct's constructor could be marked hot only by marking the whole type, which held every
  other member of the type to the same rules (#72).

## [0.1.0-preview.5] - 2026-09-27

### Fixed

- **`DD0013`** reported members whose signature the type does not choose: `Equals(object)`,
  `GetHashCode()` and `ToString()` overrides, and implementations of framework interfaces such as
  `IComparable<T>.CompareTo`. A hand-written wrapper following DD0014's value-equality advice was
  reported for doing so. Overrides of members declared in another assembly, and implementations of
  interfaces declared in another assembly, are now boundary members
  (`PrimitiveFreeSurfaces.BoundaryMembersExempt`, amended); the consumer's own interfaces and base
  classes stay checked ([#59](https://github.com/Hafeok/decision-driven-analyzers/issues/59)).
- **`DD0013`, `DD0014`, `DD0015`, `DD0016` and `DD0019`** read a model type's visibility from its
  declaration alone, so `public` members of a `private` nested record, or of an `internal` class, in
  a `[DomainModel]` namespace were reported as model surface, and DD0019 checked a `public` type
  nested inside an internal one. A model type is now one that is externally visible: it and every
  type containing it public (`PrimitiveFreeSurfaces.NoNakedPrimitivesOnModelAndContract` and
  `ImmutableModel.DomainModelImmutable`, amended)
  ([#62](https://github.com/Hafeok/decision-driven-analyzers/issues/62)).
- **`DD0016`** honoured `[DesignDecision]` only on the member owning the `bool`, so a positional
  record's `bool` had no exception path: its primary constructor cannot carry an attribute. The
  citation now answers from the member or from its type, as it does for DD0013
  (`PrimitiveFreeSurfaces.FlagArgumentsWarning`, amended)
  ([#64](https://github.com/Hafeok/decision-driven-analyzers/issues/64)).
- **`DD0010`** offered "mark it itself `[Contract]`" for a struct or an enum of the current assembly,
  which `ContractAttribute` cannot be applied to (CS0592). For a struct or an enum both paths now
  point at the model: move it into a namespace already declared, or declare the one it is in
  (`Contracts.ContractVocabularyAllowList`, amended)
  ([#63](https://github.com/Hafeok/decision-driven-analyzers/issues/63)).

## [0.1.0-preview.4] - 2026-09-26

**Breaking for a ledger with a key equal to its set's generated class name, or to `SetId`.** The
build fails with `DDGEN0005` instead of `CS0542`; see below.

### Fixed

- **`DDGEN0005`** (new). A decision key equal to its set's generated class name, or to `SetId`,
  compiled to a nested class the compiler rejects (CS0542, CS0102) inside generated code, failing
  every consuming project at a line of a file nobody wrote. The generator now reports it against the
  ledger and leaves that one decision out, so the rest of the namespace still compiles
  ([#52](https://github.com/Hafeok/decision-driven-analyzers/issues/52)). **Breaking** for a ledger
  that has such a key: the build fails with `DDGEN0005` instead of `CS0542`.
- **Generated types collided across `InternalsVisibleTo`** (CS0436). Every compilation gets its own
  `internal` copy of the attributes, `ExceptionScope` and the decision types, so a test assembly that
  sees its library's internals saw two of each, and the compiler warned on every use, an error under
  warnings as errors. Every generated type is now `[Microsoft.CodeAnalysis.Embedded]`, which the
  compiler never imports into another compilation. Rules still match the attributes by full name
  ([#51](https://github.com/Hafeok/decision-driven-analyzers/issues/51)).
- **`DD0008`** reported a package's content files. A test framework compiles helper sources from its
  package into every test project, headed `<auto-generated>`, and DD0008 asked the consumer to
  delete a header in a file in the package cache. Code under the NuGet package root is now counted
  with generated code (`RuleTiers.GeneratedCodeIsExempt`, amended), read from `NuGetPackageRoot`,
  which the package makes visible to the analyzers
  ([#50](https://github.com/Hafeok/decision-driven-analyzers/issues/50)).
- **`DD0017`** could never count an abstract record hierarchy as closed. The compiler gives every
  non-sealed record a `protected` copy constructor and forbids declaring it narrower, so an abstract
  record base with a `private protected` constructor and sealed leaves was reported on every switch.
  The copy constructor is now set aside when deciding closedness
  (`Hierarchies.ClosedHierarchiesAreSealed`, amended; the narrow door it leaves is stated on the
  rule page) ([#54](https://github.com/Hafeok/decision-driven-analyzers/issues/54)).

## [0.1.0-preview.3] - 2026-09-25

**Breaking for a consumer with a hand-written file that claims to be generated.** DD0008 now reports
it; see below.

### Changed

- **`DD0008` reports every request to look away** (`RuleTiers.GeneratedCodeIsExempt`, amended).
  Code is generated only if a source generator produced it in this compilation, its file is named
  `*.g.cs`, `*.generated.cs` or `*.designer.cs`, or it lies in the project's intermediate output
  directory. Anywhere else, an `<auto-generated>` header, `[GeneratedCode]` on any symbol,
  `generated_code = true` in `.editorconfig`, or a name only Roslyn counts as generated
  (`*.g.i.cs`, `TemporaryGeneratedFile_*`) hid the file or symbol from every other rule with no
  citation; each is now a DD0008 error. The package makes `IntermediateOutputPath` visible to the
  analyzers to recognise build-task output such as the SDK's `AssemblyInfo.cs`.

- **The generated-code exemption is a decision.** Every rule but DD0008 has always skipped
  generated code; `RuleTiers.GeneratedCodeIsExempt` now says so, with DD0008 as the stated
  exception, and a test holds every analyzer in the package to it.

- **A published package is the tested package.** Publishing used to rebuild and repack from the
  tag, and a tag push published without waiting for CI; `0.1.0-preview.2` was pushed while CI on
  its commit was still running, as bytes the samples job never saw. The publish run now runs the
  full check set first and pushes the packages that run built and tested, and only once every check
  has passed and the `nuget` environment's approval is given (`release-process`).

### Fixed

- **`DD0008`** reported nothing in generated code. It asked Roslyn to analyse generated files but
  not to report in them, so a `#pragma warning disable` for a DD rule in a `*.g.cs` file or one
  starting with `// <auto-generated/>` went unseen - in the one place every other rule does not
  look. It now reports there.

## [0.1.0-preview.2] - 2026-09-25

The third prerelease: the evidence that the package works as a package, the three defects that
evidence found in `0.1.0-preview.1`, and the whole-graph report. No rule is added and no severity
is raised.

### Added

- **The samples job tests the package.** `samples/Consumer` builds against the `DecisionDriven.Analyzers`
  nupkg the build job produced, and a violating sample per rule must report each of DD0001-DD0019
  and DDGEN0001 exactly once, with the conforming build silent. It found three defects the in-memory
  tests could not, all in rules new in `0.1.0-preview.1` and all shipped in it; they are under
  *Fixed* below.
- **`DecisionDriven.Report`**, the whole-graph report, as a .NET tool. Reads built assemblies as
  metadata, never loading them, plus the repository's git history and ledger. Per layered assembly:
  Ca, Ce, instability, abstractness and distance from the main sequence, marking an assembly less
  stable than one above it. Per `[Contract]` interface: members, implementers, and what each caller
  uses. Per `[DomainModel]` type: LCOM4. And the citation projection: one `ledger:Citation` per
  citing symbol as N-Triples, dated by the commit that introduced it and tied to the decision's tip
  as the ledger stood then, with a Markdown summary of uncited decisions, citations of a version
  that is no longer the tip, and decisions newly cited since a ref. Report-only; nothing gates.

### Changed

- **`DecisionDriven.Report` produces a report.** With no arguments it still prints its version, as
  earlier versions did; given `--assembly` it now writes `report.md` and `citations.nt`.

### Fixed

- **`DD0007`** rejected every citation of a real decision in every consumer. A real build roots
  generator output in the compiler's output directory; the provenance check matched from the
  start of the path, which only an in-memory driver produces. It now anchors on the directory this
  compilation's generator run used, which is also a tighter check than the path shape.
- **`DD0008`** reported a `#pragma` inside an inactive `#if` branch, which suppresses nothing.
- **`DD0017`** warned on an `internal` base with every leaf sealed, which nobody outside the
  assembly can derive from.

## [0.1.0-preview.1] - 2026-09-25

The second prerelease: the remaining thirteen rules.

**Breaking for a consumer that builds with warnings as errors.** Thirteen rules are new. DD0007 to
DD0015, DD0018 and DD0019 are errors; DD0016 and DD0017 are tier-2 warnings, which warnings as
errors makes errors too. Adopting this version in a project with `ArchLayer` declared will report
every public interface without `[Contract]` (DD0009), and every contract signature naming a type of
its own assembly outside a `[DomainModel]` namespace (DD0010): declare `[DomainModel]` before adding
`[Contract]`, which the README's quick start now says first. DD0018 runs in every non-test project
whether or not it declares a layer.

**Known defects.** This version was tagged before the samples job existed, and carries the three
defects that job then found, fixed in the next prerelease. The first one makes it unusable for a
consumer that cites any decision: **DD0007** reports every citation of a real decision in a real
build, and it is an error that DD0008 does not allow to be suppressed. **DD0008** also reports a
`#pragma` inside an inactive `#if` branch, and **DD0017** warns on an `internal` base whose leaves
are all sealed. Skip this version.

### Added

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
- **`DD0013`** - no naked primitives on `[Contract]` members or externally visible members of
  `[DomainModel]` types. Arrays, tasks and sequences are looked through; spans and memories are not,
  whatever they hold. `bool`, enums and type parameters are never banned. The parse and format
  boundary, the wrapper's own constructors and factories, and `[HotPath]` members are exempt, as is
  a wrapper's own primitive on its own members. The list is extended, never shortened, with `dd_banned_primitive_types_add`: a list a
  consumer could shorten would be a suppression path around the rule with no citation anywhere.
- **`DD0014`** - a `[DomainModel]` type wrapping one primitive is a `readonly record struct`, or a
  `readonly struct` with value equality. A class, a mutable struct and a struct relying on the
  default `ValueType.Equals` each get their own sentence.
- **`DD0015`** - no `implicit operator` to or from a banned primitive on a `[DomainModel]` type.
  Explicit operators and named accessors are how the conversion stays visible at the call site.
- **`DD0016`** (tier 2, **warning**) - a `bool` parameter on a contract or public model member.
  `out`/`ref` bools, delegates returning `bool` and `bool` returns are not flags. The first tier-2
  rule, with its false-positive story written before the analyzer.
- **`DD0017`** (tier 2, **warning**) - a `switch` testing two or more subtypes of a base nothing
  closed. Closed means a `private` or `file` constructor, or an `internal` one with every derived
  type in the compilation sealed. Framework hierarchies are excluded by default. A throwing discard
  arm still warns: it moves the failure from the build to a user, which is the defect rather than a
  defence against it.
- **`DD0018`** - `NotImplementedException` anywhere in a non-test assembly, whether or not the
  project has declared a layer: unlike the contract rules it is about the code, not the surface. Its second path is not
  `[DesignDecision]`: a deliberate stub is a `[DesignDecision]`-marked `NotSupportedException`,
  which DD0012 tracks and the report tool lists.
- **`DD0019`** - public types in `[DomainModel]` namespaces are immutable: `init` setters only, no
  non-`readonly` fields, no mutable collection members (arrays included), readonly structs. A sealed
  `*Builder` in the same namespace is the escape hatch, and DD0010 now keeps builders off contract
  signatures, which `BuildersAreTheEscapeHatch` requires and ADR-A11 wrongly assumed was already so.

### Changed

- **The generator marks what it emits.** Every set and decision type now carries
  `[System.CodeDom.Compiler.GeneratedCode("DecisionDriven.Analyzers", "<version>")]`, which is half
  of how DD0007 tells a generated decision from a hand-written one.
- **The rule pages say how to apply their fixes**, or that a rule has none and why, for every rule.

## [0.1.0-alpha.0.21] - 2026-09-24

The first prerelease. Nothing had been published before it.

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
- **The code-fixes assembly.** `DecisionDriven.Analyzers.CodeFixes`, packed alongside the analyzers
  in `analyzers/dotnet/cs`: the documented-exception placeholder, which does not compile by design,
  for `DD0002` and `DD0003`, and the design-change fix for `DD0002`. (This entry first said the
  placeholder covered every DD rule. It never did: it is registered for DD0001 to DD0003, and DD0001
  reports on the compilation, where there is no declaration to put it on.) Apply them from a terminal with
  `dotnet format analyzers --diagnostics <id>`.
- **`DDBUILD0001`** - the Roslyn pin matches the floor the analyzers declare, so a dependency bump
  cannot quietly drop support for the oldest SDK in the band.
- **`DDBUILD0002`** - no package is produced without its code-fixes assembly.
- **`DDGEN0001`-`DDGEN0004`** - the decision input is well formed: no duplicate key in a namespace,
  no key that is not an identifier, no key changed between versions, no unparseable export line.
- `DecisionDriven.Report`, a .NET tool, reporting its version.

### Notes for consumers

Every decision in this repository's own `docs/decisions/` is unaccepted, which is the mechanism
working rather than an oversight: citing one produces `CS0618` on every citation, so they are usable
on a branch and will not ship under `TreatWarningsAsErrors`.

[Unreleased]: https://github.com/Hafeok/decision-driven-analyzers/compare/v0.1.0-preview.7...main
[0.1.0-preview.7]: https://github.com/Hafeok/decision-driven-analyzers/tree/v0.1.0-preview.7
[0.1.0-preview.6]: https://github.com/Hafeok/decision-driven-analyzers/tree/v0.1.0-preview.6
[0.1.0-preview.5]: https://github.com/Hafeok/decision-driven-analyzers/tree/v0.1.0-preview.5
[0.1.0-preview.4]: https://github.com/Hafeok/decision-driven-analyzers/tree/v0.1.0-preview.4
[0.1.0-preview.3]: https://github.com/Hafeok/decision-driven-analyzers/tree/v0.1.0-preview.3
[0.1.0-preview.2]: https://github.com/Hafeok/decision-driven-analyzers/tree/v0.1.0-preview.2
[0.1.0-preview.1]: https://github.com/Hafeok/decision-driven-analyzers/tree/v0.1.0-preview.1
[0.1.0-alpha.0.21]: https://github.com/Hafeok/decision-driven-analyzers/tree/acb256b8378b848fac5d16bf1d285959b81e2e38
