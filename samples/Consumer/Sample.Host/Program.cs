using System;
using Sample.Layer0;
using Sample.Layer1;
using Sample.Layer2;

namespace Sample.Host;

/// <summary>
/// The composition root: the only place that names every layer.
/// </summary>
internal static class Program
{
    internal static void Main()
    {
        Store store = new();
        Service service = new(store);
        Workflow workflow = new(service);

        Console.Out.WriteLine(workflow.Describe());
    }
}
