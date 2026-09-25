#if DD_SAMPLE_VIOLATIONS
namespace Sample.Layer2.Violations;

// DD0007: a citation that names a type the generator did not emit.
/// <summary>A contract citing a type that is not a decision.</summary>
[global::DecisionDriven.Contract(typeof(string), Role = "cites nothing")]
public interface ICitesAString
{
}
#endif
