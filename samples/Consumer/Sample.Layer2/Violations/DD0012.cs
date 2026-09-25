#if DD_SAMPLE_VIOLATIONS
namespace Sample.Layer2.Violations;

// DD0012: an implemented member that throws NotSupportedException.
internal sealed class Refuses : global::System.IDisposable
{
    public void Dispose() => throw new global::System.NotSupportedException();
}
#endif
