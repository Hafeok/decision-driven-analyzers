#if DD_SAMPLE_VIOLATIONS
namespace Sample.Layer2.Violations;

// DD0017: a switch over two subtypes of a hierarchy nothing closed - Square is not sealed.
internal abstract class Shape
{
}

internal sealed class Circle : Shape
{
}

internal class Square : Shape
{
}

internal static class Area
{
    internal static int Of(Shape shape) => shape switch
    {
        Circle => 1,
        Square => 2,
        _ => 0,
    };
}
#endif
