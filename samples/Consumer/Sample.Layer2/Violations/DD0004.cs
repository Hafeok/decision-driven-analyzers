#if DD_SAMPLE_VIOLATIONS
namespace Sample.Layer2.Violations;

// DD0004: mutable static state.
internal static class Counter
{
    internal static int Count;
}
#endif
