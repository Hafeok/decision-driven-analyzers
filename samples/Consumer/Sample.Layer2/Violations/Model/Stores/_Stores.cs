#if DD_SAMPLE_VIOLATIONS
// Not a violation: a sub-namespace of the [DomainModel] namespace _Model.cs declares with
// IncludeSubNamespaces = false. The store here is mutable, and is not model; if the package took
// sub-namespaces in, it would be DD0014's and DD0019's finding a second time.
namespace Sample.Layer2.Violations.Model.Stores;

/// <summary>A mutable store beside the model, not in it.</summary>
public sealed class Store
{
    /// <summary>Gets or sets how many items it holds.</summary>
    public int Count { get; set; }
}
#endif
