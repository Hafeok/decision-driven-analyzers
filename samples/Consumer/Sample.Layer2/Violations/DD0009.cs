#if DD_SAMPLE_VIOLATIONS
namespace Sample.Layer2.Violations;

// DD0009: a public interface that cites no decision.
/// <summary>A public interface nobody decided to expose.</summary>
public interface IUndeclared
{
}
#endif
