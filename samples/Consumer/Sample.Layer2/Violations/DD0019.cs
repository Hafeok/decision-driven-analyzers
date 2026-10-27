#if DD_SAMPLE_VIOLATIONS
namespace Sample.Layer2.Model;

// DD0019: a model type with a public setter.
/// <summary>A model type a caller can change.</summary>
public sealed class Source
{
    /// <summary>Gets or sets where it came from.</summary>
    public global::System.Uri? Location { get; set; }
}

// DD0019, the second of two (violations.expected says "DD0019 2"): a published-once set written to
// after it is published (ImmutableModel.MemoizedFieldsAreVerified). A reader that found the set
// already published can see it change.
/// <summary>Places sources may not come from, published once and then changed.</summary>
public sealed class Blocklist
{
    [global::DecisionDriven.Memoized]
    private global::System.Collections.Generic.HashSet<global::System.Uri>? blocked;

    /// <summary>Blocks a place.</summary>
    /// <param name="location">The place.</param>
    public void Block(global::System.Uri location)
    {
        global::System.Collections.Generic.HashSet<global::System.Uri> built = new() { location };
        blocked ??= built;
        built.Add(location);
    }

    /// <summary>Whether a place is blocked.</summary>
    /// <param name="location">The place.</param>
    /// <returns>True when it is blocked.</returns>
    public bool Blocks(global::System.Uri location) => blocked is not null && blocked.Contains(location);
}

// Not a violation: a set filled in the constructor and only read afterwards. HashSet<T>.Contains
// implements IReadOnlySet<T>.Contains, so DD0019 takes it for the read it is; reporting it would
// make DD0019 appear a third time, which violations.expected's count of two catches.
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

// Not a violation: a set built in a local on first use and published once, then only read
// (ImmutableModel.MemoizedFieldsAreVerified, the published-once form). Two first readers may each
// build one; each fills only its own local, and one publication wins.
/// <summary>The places sources may come from, looked up in a set built on first use.</summary>
public sealed class Allowlist
{
    private readonly global::System.Collections.Immutable.ImmutableArray<global::System.Uri> allowed;

    [global::DecisionDriven.Memoized]
    private global::System.Collections.Generic.HashSet<global::System.Uri>? lookup;

    /// <summary>Initializes a new instance of the <see cref="Allowlist"/> class.</summary>
    /// <param name="allowed">The places allowed.</param>
    public Allowlist(global::System.Collections.Immutable.ImmutableArray<global::System.Uri> allowed) => this.allowed = allowed;

    /// <summary>Whether a source from <paramref name="location"/> is allowed.</summary>
    /// <param name="location">Where the source came from.</param>
    /// <returns>True when it is one of the places allowed.</returns>
    public bool Allows(global::System.Uri location)
    {
        if (lookup is null)
        {
            global::System.Collections.Generic.HashSet<global::System.Uri> built = new();

            foreach (global::System.Uri place in allowed)
            {
                built.Add(place);
            }

            lookup ??= built;
        }

        return lookup.Contains(location);
    }
}

// Not a violation: three memos DD0019 verifies (ImmutableModel.MemoizedFieldsAreVerified). Each is
// written after the value is handed out, and none changes what a reader sees. Reporting any of them
// would make DD0019 appear more than twice, which violations.expected's count of two catches; and
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
