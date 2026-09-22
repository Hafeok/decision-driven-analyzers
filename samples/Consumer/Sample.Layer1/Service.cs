using Sample.Layer0;

namespace Sample.Layer1;

/// <summary>
/// Layer 1: depends on layer 0 and on nothing above it.
/// </summary>
public sealed class Service
{
    private readonly Store store;

    /// <summary>
    /// Initializes a new instance of the <see cref="Service"/> class.
    /// </summary>
    /// <param name="store">The store this service reads from.</param>
    public Service(Store store) => this.store = store;

    /// <summary>
    /// Gets the name of the store behind this service.
    /// </summary>
    public string StoreName => store.Name;
}
