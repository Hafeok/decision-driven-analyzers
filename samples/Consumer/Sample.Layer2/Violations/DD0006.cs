#if DD_SAMPLE_VIOLATIONS
namespace Elsewhere;

// DD0006: a public type outside the assembly's root namespace. A namespace outside the root cannot
// name this file's folder either, so DD0022 reports this file as well: violations.expected counts
// DD0022 twice, once here and once in DD0022.cs.
/// <summary>A type outside the root namespace.</summary>
public sealed class Stray
{
}
#endif
