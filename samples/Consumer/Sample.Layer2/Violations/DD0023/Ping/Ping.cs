#if DD_SAMPLE_VIOLATIONS
namespace Sample.Layer2.Violations.DD0023.Ping;

// DD0023, one of two (violations.expected says "DD0023 2"): this namespace refers to Pong, and
// Pong refers back. One finding per edge of the cycle, so one here and one in Pong/Pong.cs.
/// <summary>Half of a cycle.</summary>
public sealed class Ping
{
    /// <summary>Gets the other half.</summary>
    public Pong.Pong? Other { get; init; }
}
#endif
