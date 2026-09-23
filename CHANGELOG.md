# Changelog

All notable changes to this project are documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

Rule changes are listed by diagnostic id. Adding a rule, raising a rule's default
severity, and promoting a rule from tier 2 to tier 1 are all breaking changes for a
consumer that builds with warnings as errors, and are recorded as such.

## [Unreleased]

### Added

- Repository scaffolding: licence, contributor path, governance, security policy, issue
  and pull request templates, and an editor configuration.
- An empty solution that builds, tests and packs: the analyzer package with its MSBuild
  props, the `DecisionDriven.Report` tool, test projects, and a sample consumer.
- Tag-driven versioning with MinVer: a `v*` tag is a release version and every other
  build of the trunk is a prerelease.
- Continuous integration, and a publish workflow using NuGet trusted publishing.
- The rule page format and its template, in `docs/rules/`.
- The decision ledger source generator: the marker attributes (`ArchLayer`, `Contract`,
  `DomainModel`, `HotPath`, `DesignDecision`) and `ExceptionScope` emitted into each
  consuming compilation, and one nested type per decision so that a citation is a symbol
  reference the compiler checks. Reads the ledger's N-Triples export and, until that
  exists, the interim markdown front matter.
- `DD0001`: a reference within a family points strictly downward, read from the referenced
  assembly's metadata so package references are checked like project references.
- `DD0002`: every `InternalsVisibleTo` target is a test assembly.
- `DD0003`: services are resolved only in the composition root.
- `DDGEN0001`-`DDGEN0004`: generator errors for a duplicate decision key, a key that is
  not an identifier, a key changed between versions, and an unparseable export line.

No rules are shipped yet and nothing has been published to NuGet.

[Unreleased]: https://github.com/Hafeok/decision-driven-analyzers/commits/main
