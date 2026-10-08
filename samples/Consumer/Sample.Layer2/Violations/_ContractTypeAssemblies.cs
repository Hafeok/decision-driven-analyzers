#if DD_SAMPLE_VIOLATIONS
namespace Sample.Layer2.Violations;

// Not a violation: a contract naming a type from each assembly in ArchContractTypeAssemblies,
// Sample.Layer0 and Sample.Layer1. It conforms only if the whole list reaches DD0010, so a package
// that cut the list at its first ';' would report Sample.Layer1.Service here, and violations.expected
// would see DD0010 twice. Under the define because a [Contract] cites a decision, and every decision
// here is unaccepted: CS0618, which only the violating build silences.
/// <summary>A contract naming a type from each listed assembly.</summary>
[global::DecisionDriven.Contract(typeof(global::DecisionDriven.Ledger.DddAnalyzers.Contracts.ContractVocabularyAllowList), Role = "names both listed assemblies")]
public interface INamesBothListedAssemblies
{
    /// <summary>Returns a type from the first listed assembly.</summary>
    /// <returns>The store.</returns>
    global::Sample.Layer0.Store Store();

    /// <summary>Returns a type from the second listed assembly.</summary>
    /// <returns>The service.</returns>
    global::Sample.Layer1.Service Service();
}
#endif
