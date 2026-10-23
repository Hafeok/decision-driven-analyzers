#if DD_SAMPLE_VIOLATIONS
namespace Sample.Layer2.Model;

// DD0019: a model type with a public setter.
/// <summary>A model type a caller can change.</summary>
public sealed class Source
{
    /// <summary>Gets or sets where it came from.</summary>
    public global::System.Uri? Location { get; set; }
}

// Not a violation: a set filled in the constructor and only read afterwards. HashSet<T>.Contains
// implements IReadOnlySet<T>.Contains, so DD0019 takes it for the read it is; reporting it would
// make DD0019 appear twice, which violations.expected's "exactly once" catches.
/// <summary>The places sources may come from.</summary>
public sealed class Origins
{
    private readonly global::System.Collections.Generic.HashSet<global::System.Uri> allowed;

    /// <summary>Initializes a new instance of the <see cref="Origins"/> class.</summary>
    /// <param name="allowed">The places allowed.</param>
    public Origins(global::System.Collections.Generic.IEnumerable<global::System.Uri> allowed) => this.allowed = new(allowed);

    /// <summary>Whether a source from <paramref name="location"/> is allowed.</summary>
    /// <param name="location">Where the source came from.</param>
    /// <returns>True when it is one of the places allowed.</returns>
    public bool Allows(global::System.Uri location) => allowed.Contains(location);
}
#endif
