#if DD_SAMPLE_VIOLATIONS
namespace Sample.Layer2.Violations.DD0023.Pong;

// DD0023, the other of two: this namespace refers back to Ping.
/// <summary>The other half of a cycle.</summary>
public sealed class Pong
{
    /// <summary>Gets the first half.</summary>
    public Ping.Ping? Other { get; init; }
}
#endif
