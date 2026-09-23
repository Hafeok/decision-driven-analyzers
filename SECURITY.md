# Security policy

## Reporting a vulnerability

Report vulnerabilities privately through GitHub security advisories:

**[Report a vulnerability](https://github.com/Hafeok/decision-driven-analyzers/security/advisories/new)**

Do not open a public issue for a vulnerability, and do not include a working exploit in
the first message.

Please include what you can of: the affected version or commit, what an attacker gains,
the smallest reproduction you have, and whether the issue is already public elsewhere.

You should get an acknowledgement within a week. This is a single-maintainer project, so
a fix may take longer than that; you will be told what is happening either way. Credit is
given in the advisory unless you ask otherwise.

## Scope

This package is a development-time analyzer package. It runs inside the compiler and the
IDE on the machine of whoever builds a consuming project, and it never ships in a
consumer's runtime output (`TwoPackages.DevelopmentTimeOnly`). The interesting attack
surface is therefore build-time: what the analyzers read from `AdditionalFiles` and
MSBuild properties, what the source generator emits into a consumer's compilation, and
what `DecisionDriven.Report` does with a solution it is pointed at.

In scope:

- Code execution or file access beyond the compilation, triggered by analyzer input such
  as a decision set file, an `.editorconfig` option or an MSBuild property.
- Generated code that changes the meaning of a consumer's program in a way the consumer
  cannot see.
- Anything that causes the package to appear in a consumer's runtime output.

Out of scope:

- A rule that produces a false positive or a false negative. That is a bug; open a normal
  issue with the false-positive template.
- Denial of service against a build by handing the analyzers a deliberately pathological
  source file.

## Supported versions

Nothing has been published yet, so no version is supported.

| Version | Supported |
| --- | --- |
| _none published_ | — |

This table is filled in with the first release. Until then, the only thing to report
against is `main`.
