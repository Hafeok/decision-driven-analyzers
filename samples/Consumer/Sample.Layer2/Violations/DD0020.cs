#if DD_SAMPLE_VIOLATIONS
namespace Sample.Layer2.Violations;

// DD0020: dynamic as a type in a layered project.
internal static class Relay
{
    internal static object Wrap(object value) => (dynamic)value;
}
#endif
