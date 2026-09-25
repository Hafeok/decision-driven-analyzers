#if DD_SAMPLE_VIOLATIONS
namespace Sample.Layer2.Violations;

// DD0011: a contract parameter that is a collaborator - a service provider.
/// <summary>A contract taking a service provider.</summary>
[global::DecisionDriven.Contract(typeof(global::DecisionDriven.Ledger.DddAnalyzers.Contracts.DataVersusCollaboratorParameters), Role = "takes a provider")]
public interface ITakesAProvider
{
    /// <summary>Takes a collaborator per call.</summary>
    /// <param name="services">A provider to resolve from.</param>
    void Run(global::System.IServiceProvider services);
}
#endif
