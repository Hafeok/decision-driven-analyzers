#if DD_SAMPLE_VIOLATIONS
namespace Sample.Layer2.Elsewhere;

// DD0022: a namespace that does not name the file's folder. This file is Violations/DD0022.cs,
// whose folder names Sample.Layer2.Violations; it declares Sample.Layer2.Elsewhere, under the
// root, so DD0006 has nothing to say and DD0022 is the one finding.
/// <summary>A type whose namespace says it lives somewhere else.</summary>
public sealed class Misplaced
{
}
#endif
