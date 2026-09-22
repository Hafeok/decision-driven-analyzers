# DecisionDriven.Analyzers.Package

The MSBuild half of the `DecisionDriven.Analyzers` package. These two files are packed
into the nupkg under `build/` and are imported by every project that references the
package; they are not compiled and produce no assembly, which is why this directory holds
no project file.

| File | Imported | Does |
| --- | --- | --- |
| `DecisionDriven.Analyzers.props` | before the consuming project's body | makes `ArchFamily`, `ArchLayer`, `ArchCompositionRoot` and `ArchContractTypeAssemblies` visible to the compiler, and makes `DdLedger` metadata on `AdditionalFiles` visible to analyzers |
| `DecisionDriven.Analyzers.targets` | after the consuming project's body | adds the decision set files and the ledger export to `AdditionalFiles`, tagged with `DdLedger` |

A consumer configures the rules like this, and never by changing analyzer code
(`two-packages.ConfigurationViaMsBuildProperties`):

```xml
<PropertyGroup>
  <ArchFamily>Sample</ArchFamily>
  <ArchLayer>1</ArchLayer>
  <ArchCompositionRoot>false</ArchCompositionRoot>
  <ArchContractTypeAssemblies>Sample.Contracts</ArchContractTypeAssemblies>
  <DdLedgerDirectory>$(MSBuildThisFileDirectory)../../docs/decisions</DdLedgerDirectory>
</PropertyGroup>
```

`samples/Consumer/` in this repository is a worked example of exactly that.

A ledger that is not one flat directory skips `DdLedgerDirectory` and adds the files
itself, with the metadata the analyzers read:

```xml
<ItemGroup>
  <AdditionalFiles Include="../decisions/**/*.md" DdLedger="decision-set" />
  <AdditionalFiles Include="../decisions/export.nt" DdLedger="ledger-export" />
</ItemGroup>
```
