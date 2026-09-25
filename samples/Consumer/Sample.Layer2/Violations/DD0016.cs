#if DD_SAMPLE_VIOLATIONS
namespace Sample.Layer2.Violations;

// DD0016: a flag argument on a contract. Tier 2, so a warning - an error here only because the
// samples build with warnings as errors.
/// <summary>A contract with a flag argument.</summary>
[global::DecisionDriven.Contract(typeof(global::DecisionDriven.Ledger.DddAnalyzers.PrimitiveFreeSurfaces.FlagArgumentsWarning), Role = "switches on a flag")]
public interface IToggles
{
    /// <summary>Switches on a flag.</summary>
    /// <param name="on">The flag.</param>
    void Toggle(bool on);
}
#endif
