# samples/Consumer

A consumer, built the way a consumer builds. The analyzers are loaded into every project
here, `docs/decisions/` arrives as `AdditionalFiles`, and the four `Arch*` properties
describe the shape the rules are meant to check.

| Project | `ArchLayer` | `ArchCompositionRoot` | References |
| --- | --- | --- | --- |
| `Sample.Layer0` | 0 | false | — |
| `Sample.Layer1` | 1 | false | `Sample.Layer0` |
| `Sample.Layer2` | 2 | false | `Sample.Layer1` |
| `Sample.Host` | — | **true** | all three layers |
| `Sample.Layer1.Tests` | 1 | false | `Sample.Layer1` |

All five set `ArchFamily=Sample`. `Sample.Host` is the composition root, which is why it
is allowed to name every layer at once; `Sample.Layer1.Tests` is at the layer of the code
it tests.

`Consumer.slnx` gathers all five so that CI compiles every one of them. `Sample.Host`
does not reference `Sample.Layer1.Tests`, so building the host alone would quietly leave
the test sample out of the only thing it is for.

Everything outside `Violations/` conforms. That is the point: a diagnostic on any of it is a
false positive in the rule until shown otherwise (`RuleTiers.SamplesJobIsTheFalsePositiveCheck`).

## Two modes

| | How the analyzers arrive | Who uses it |
| --- | --- | --- |
| **Project mode** (default) | A project reference to `src/DecisionDriven.Analyzers`, and its props and targets imported from the working tree | `dotnet build DecisionDriven.slnx`, so the solution builds without a pack first |
| **Package mode** (`-p:DdAnalyzersPackageVersion=<ver>`) | A `PackageReference` to `DecisionDriven.Analyzers` from `artifacts/packages`, exactly as the README tells a consumer to write it | The CI samples job |

Project mode proves the rules. Only package mode proves the package: that the nupkg's analyzer and
code-fix assemblies, its `build/` props and targets and the generator inside it load in a consuming
build and fire. Every test in `tests/` constructs the analyzers in memory, so nothing else shows that.

## The violating samples

One file per rule under `Sample.Layer2/Violations/`, each wrapped in `#if DD_SAMPLE_VIOLATIONS` and
breaking exactly one rule. Two violations are not source files, because of what they are about:

- **DD0001** is about references, so it is `Sample.Layer1.csproj` moving the project down to layer
  0 under the define, which makes its reference to `Sample.Layer0` point sideways.
- **DDGEN0001** is about ledger input, so it is `Sample.Layer0/Violations/DDGEN0001.md`, a set file
  with two decisions claiming one key, added to that project's `AdditionalFiles` under the define.

`_Model.cs` is not a violation: it declares the `[DomainModel]` namespace the model-surface
violations need.

The violating samples cite this repository's own decisions, which are all unaccepted until the
maintainer accepts them, so each citation is CS0618. Under the define only, CS0618 is not reported;
the conforming build cites nothing and keeps it.

## Running the check

```
dotnet pack DecisionDriven.slnx -c Release -o artifacts/packages
samples/Consumer/check-violations.sh <version>
```

It restores the package into a fresh folder and checks the restored bytes match the packed ones;
builds the conforming samples, which must be silent; builds each project with the define, which
must report every id in `violations.expected` exactly once and nothing else; and runs DDBUILD0001
against the analyzer project, since that check guards this repository's Roslyn pin and does not
travel in the package. Logs land in `artifacts/samples-logs/`.

A missing id is a packaging defect until shown otherwise: the rule passes its in-memory tests, so
the package is not what they tested.
