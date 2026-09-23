# DDBUILD0002: Do not ship the package without its code-fixes assembly

|  |  |
| --- | --- |
| **Id** | `DDBUILD0002` |
| **Tier** | 1 (error) |
| **Severity** | error (an MSBuild error; no package is produced) |
| **Motivating decision** | `TwoPackages.CodeFixesShipInSeparateAssembly` |
| **Introduced in** | unreleased |

## Principle

A rule whose documented-exception path cannot be applied is a rule with one path. The fixes are half
of what every DD rule offers, and they ship in a different assembly from the analyzers, so nothing
about a successful analyzer build says they are there.

## What the rule checks

At pack time, that `DecisionDriven.Analyzers.CodeFixes.dll` exists where the packaging step expects
it. Both assemblies go into `analyzers/dotnet/cs` of the one package: the compiler loads the analyzer
and ignores the fixes, while an IDE or `dotnet format analyzers` loads both.

This is a build-target check rather than an analyzer, which `RuleTiers.BuildTargetChecksAreTierOne`
makes a tier-1 form, in the `DDBUILD` family of `RuleTiers.IdFamilies`.

## Configuration

| Setting | Where | Default | Effect |
| --- | --- | --- | --- |
| `CodeFixesAssemblyPath` | MSBuild property | the code-fixes project's `bin/$(Configuration)/netstandard2.0/` output | Where the assembly is expected |

The default is derived from the project's configuration and target framework. The property exists so
that the check can be exercised — pointing it somewhere empty is how the rule is tested — and so
that a change to the output layout has a knob rather than needing an edit to the packaging target.

## False-positive story

Decidable: the file is either there or it is not. It can be wrong about *where* to look — the path
is derived rather than resolved through the project reference, so a change to the output layout
would make it report a file that exists somewhere else. That is the failure this is meant to be
loud about: the alternative is a package that silently ships without its fixes.

## Violating example

Packing after the code-fixes project failed to build, or after its output moved:

```text
DDBUILD0002: The code-fixes assembly was not found at .../DecisionDriven.Analyzers.CodeFixes.dll.
TwoPackages.CodeFixesShipInSeparateAssembly puts it in the same package as the analyzers, so a
package without it silently ships rules whose documented-exception path cannot be applied.
```

## Conforming example

```text
analyzers/dotnet/cs/DecisionDriven.Analyzers.dll
analyzers/dotnet/cs/DecisionDriven.Analyzers.CodeFixes.dll
build/DecisionDriven.Analyzers.props
build/DecisionDriven.Analyzers.targets
```

CI asserts this layout on every build, along with the absence of `lib/`.

## See also

- `docs/decisions/two-packages.md` — `CodeFixesShipInSeparateAssembly`
- `docs/decisions/build-time-dependencies.md` — `WorkspacesForCodeFixesOnly`
- `DDBUILD0001` — the other build-target check
