#if DD_SAMPLE_VIOLATIONS
namespace Sample.Layer2.Violations;

// DD0010: a contract signature naming a type this contract has not agreed to name. Sample.Layer0 and
// Sample.Layer1 are both in ArchContractTypeAssemblies (see _ContractTypeAssemblies.cs), so the
// undecided type is one of this assembly's own, outside its [DomainModel] namespace.
/// <summary>A contract naming a type it has not agreed to name.</summary>
[global::DecisionDriven.Contract(typeof(global::DecisionDriven.Ledger.DddAnalyzers.Contracts.ContractVocabularyAllowList), Role = "names an undecided type")]
public interface IBorrowsVocabulary
{
    /// <summary>Returns a type this contract has not agreed to name.</summary>
    /// <returns>The current workflow.</returns>
    global::Sample.Layer2.Workflow Current();
}
#endif
