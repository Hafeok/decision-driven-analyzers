#if DD_SAMPLE_VIOLATIONS
namespace Sample.Layer2.Model;

// DD0014: a wrapper around one primitive that is a class.
/// <summary>A wrapper that allocates.</summary>
public sealed class Position
{
    /// <summary>Initializes a new instance of the <see cref="Position"/> class.</summary>
    /// <param name="value">The wrapped value.</param>
    public Position(long value) => Value = value;

    /// <summary>Gets the wrapped value.</summary>
    public long Value { get; }
}
#endif
