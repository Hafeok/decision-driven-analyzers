#if DD_SAMPLE_VIOLATIONS
namespace Sample.Layer2.Violations;

// DD0003: a service resolved at run time in a project that is not the composition root.
internal static class ServiceLocator
{
    internal static object? Find(global::System.IServiceProvider services) => services.GetService(typeof(int));
}
#endif
