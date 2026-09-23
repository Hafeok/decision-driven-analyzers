; Unshipped analyzer release
; https://github.com/dotnet/roslyn-analyzers/blob/main/src/Microsoft.CodeAnalysis.Analyzers/ReleaseTrackingAnalyzers.Help.md

### New Rules

Rule ID | Category | Severity | Notes
--------|----------|----------|-------
DDGEN0001 | DecisionDriven.Ledger | Error | Duplicate decision key in a ledger namespace
DDGEN0002 | DecisionDriven.Ledger | Error | Decision key does not match the key syntax
DDGEN0003 | DecisionDriven.Ledger | Error | Decision key changed between versions
DDGEN0004 | DecisionDriven.Ledger | Error | Unparseable line in the ledger export
DD0001 | DecisionDriven | Error | Reference does not point strictly downward, [documentation](https://github.com/Hafeok/decision-driven-analyzers/blob/main/docs/rules/DD0001.md)
DD0002 | DecisionDriven | Error | InternalsVisibleTo grants access to something that is not a test assembly, [documentation](https://github.com/Hafeok/decision-driven-analyzers/blob/main/docs/rules/DD0002.md)
DD0003 | DecisionDriven | Error | Service resolved at runtime outside the composition root, [documentation](https://github.com/Hafeok/decision-driven-analyzers/blob/main/docs/rules/DD0003.md)
