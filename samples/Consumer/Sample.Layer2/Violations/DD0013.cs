#if DD_SAMPLE_VIOLATIONS
namespace Sample.Layer2.Violations;

// DD0013: a naked primitive on a contract surface.
/// <summary>A contract with a naked primitive on it.</summary>
[global::DecisionDriven.Contract(typeof(global::DecisionDriven.Ledger.DddAnalyzers.PrimitiveFreeSurfaces.NoNakedPrimitivesOnModelAndContract), Role = "seeks by number")]
public interface ISeeks
{
    /// <summary>Seeks to a position nobody gave a name.</summary>
    /// <param name="position">A number.</param>
    void Seek(long position);
}
#endif
