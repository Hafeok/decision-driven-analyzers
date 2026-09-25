#if DD_SAMPLE_VIOLATIONS
namespace Sample.Layer2.Violations;

// DD0010: a contract signature naming a type from an assembly not in ArchContractTypeAssemblies.
/// <summary>A contract naming a type from another package.</summary>
[global::DecisionDriven.Contract(typeof(global::DecisionDriven.Ledger.DddAnalyzers.Contracts.ContractVocabularyAllowList), Role = "names another package")]
public interface IBorrowsVocabulary
{
    /// <summary>Returns a type this contract has not agreed to name.</summary>
    /// <returns>The current service.</returns>
    global::Sample.Layer1.Service Current();
}
#endif
