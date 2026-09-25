#if DD_SAMPLE_VIOLATIONS
namespace Sample.Layer2.Model;

// DD0019: a model type with a public setter.
/// <summary>A model type a caller can change.</summary>
public sealed class Source
{
    /// <summary>Gets or sets where it came from.</summary>
    public global::System.Uri? Location { get; set; }
}
#endif
