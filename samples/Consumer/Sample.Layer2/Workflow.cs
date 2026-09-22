using Sample.Layer1;

namespace Sample.Layer2;

/// <summary>
/// Layer 2: depends on layer 1, and reaches layer 0 only through it.
/// </summary>
public sealed class Workflow
{
    private readonly Service service;

    /// <summary>
    /// Initializes a new instance of the <see cref="Workflow"/> class.
    /// </summary>
    /// <param name="service">The service this workflow drives.</param>
    public Workflow(Service service) => this.service = service;

    /// <summary>
    /// Gets a one-line description of what this workflow is working against.
    /// </summary>
    /// <returns>A description naming the store at the bottom of the graph.</returns>
    public string Describe() => "workflow over " + service.StoreName;
}
