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
DD0004 | DecisionDriven | Error | Mutable static state, [documentation](https://github.com/Hafeok/decision-driven-analyzers/blob/main/docs/rules/DD0004.md)
DD0005 | DecisionDriven | Error | Grab-bag name on an assembly or namespace, [documentation](https://github.com/Hafeok/decision-driven-analyzers/blob/main/docs/rules/DD0005.md)
DD0006 | DecisionDriven | Error | Public type outside the assembly's root namespace, [documentation](https://github.com/Hafeok/decision-driven-analyzers/blob/main/docs/rules/DD0006.md)
DD0007 | DecisionDriven | Error | Citation is not a generated decision, or is missing a required argument, [documentation](https://github.com/Hafeok/decision-driven-analyzers/blob/main/docs/rules/DD0007.md)
DD0008 | DecisionDriven | Error | Suppression of a DecisionDriven rule, [documentation](https://github.com/Hafeok/decision-driven-analyzers/blob/main/docs/rules/DD0008.md)
DD0009 | DecisionDriven | Error | Public interface, abstract class or delegate with no cited decision, [documentation](https://github.com/Hafeok/decision-driven-analyzers/blob/main/docs/rules/DD0009.md)
DD0010 | DecisionDriven | Error | Contract signature names a type from an undecided package, [documentation](https://github.com/Hafeok/decision-driven-analyzers/blob/main/docs/rules/DD0010.md)
DD0011 | DecisionDriven | Error | Contract parameter is a collaborator rather than data, [documentation](https://github.com/Hafeok/decision-driven-analyzers/blob/main/docs/rules/DD0011.md)
DD0012 | DecisionDriven | Error | Implemented member throws NotSupportedException, [documentation](https://github.com/Hafeok/decision-driven-analyzers/blob/main/docs/rules/DD0012.md)
