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

// Not a violation: three memos DD0019 verifies (ImmutableModel.MemoizedFieldsAreVerified). Each is
// written after the value is handed out, and none changes what a reader sees. Reporting any of them
// would make DD0019 appear more than once, which violations.expected's "exactly once" catches; and
// if the package did not emit [Memoized], this file would not compile.
/// <summary>Where a source came from, with what is derived from it computed once.</summary>
public sealed class Provenance
{
    private readonly global::System.Uri location;

    [global::DecisionDriven.Memoized]
    private int hash;

    [global::DecisionDriven.Memoized]
    private string? text;

    [global::DecisionDriven.Memoized]
    private readonly global::System.Collections.Concurrent.ConcurrentDictionary<global::System.Uri, global::System.Uri> resolved = new();

    /// <summary>Initializes a new instance of the <see cref="Provenance"/> class.</summary>
    /// <param name="location">Where the source came from.</param>
    public Provenance(global::System.Uri location) => this.location = location;

    /// <inheritdoc/>
    public override int GetHashCode()
    {
        if (hash == 0)
        {
            hash = location.GetHashCode();
        }

        return hash;
    }

    /// <inheritdoc/>
    public override bool Equals(object? obj) => obj is Provenance other && other.location == location;

    /// <inheritdoc/>
    public override string ToString() => text ??= "from " + location.AbsoluteUri;

    /// <summary>Resolves a reference against where the source came from.</summary>
    /// <param name="relative">The reference.</param>
    /// <returns>The reference, resolved.</returns>
    public global::System.Uri Resolve(global::System.Uri relative) =>
        resolved.GetOrAdd(relative, static (r, origin) => new global::System.Uri(origin, r), location);
}
#endif
