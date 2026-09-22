using Sample.Layer0;
using Sample.Layer1;

namespace Sample.Layer1.Tests;

/// <summary>
/// Stands in for a test class. There is no test framework here on purpose: this project
/// exists to be compiled with the analyzers loaded, not to be run.
/// </summary>
public sealed class ServiceTests
{
    /// <summary>
    /// Builds the object graph a test would build.
    /// </summary>
    /// <returns>The name the service reports for its store.</returns>
    public string ServiceReportsTheStoreName()
    {
        Service service = new(new Store());

        return service.StoreName;
    }
}
