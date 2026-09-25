#if DD_SAMPLE_VIOLATIONS
namespace Sample.Layer2.Model;

// DD0015: an implicit conversion from a model type to its primitive.
/// <summary>A wrapper that converts back silently.</summary>
/// <param name="Value">The wrapped value.</param>
public readonly record struct Offset(long Value)
{
    /// <summary>The silent conversion.</summary>
    /// <param name="offset">The wrapper.</param>
    public static implicit operator long(Offset offset) => offset.Value;
}
#endif
