namespace DecisionDriven.Report.Citations;

/// <summary>A citing symbol, as the assemblies say it is.</summary>
/// <param name="Assembly">The assembly the symbol is in.</param>
/// <param name="Symbol">Its documentation-comment id, or <c>N:</c> and a namespace for <c>[DomainModel]</c>.</param>
/// <param name="Attribute">The marker, without its <c>Attribute</c> suffix: Contract, DomainModel, HotPath, DesignDecision.</param>
/// <param name="DecisionId">The cited decision's ledger id, read from the generated type's <c>Id</c> constant.</param>
/// <param name="LedgerNamespace">The cited decision's ledger namespace.</param>
/// <param name="Key">The cited decision's key.</param>
/// <param name="Scope">The <c>ExceptionScope</c> of a <c>[DesignDecision]</c>, by name; null otherwise.</param>
/// <param name="SourceFile">Where the symbol was written, as the PDB names it; null when unknown.</param>
internal sealed record Citation(
    string Assembly,
    string Symbol,
    string Attribute,
    string DecisionId,
    string LedgerNamespace,
    string Key,
    string? Scope,
    string? SourceFile);
