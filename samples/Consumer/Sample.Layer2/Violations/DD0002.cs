#if DD_SAMPLE_VIOLATIONS
// DD0002: an InternalsVisibleTo grant to an assembly that is not a test assembly.
[assembly: System.Runtime.CompilerServices.InternalsVisibleTo("Sample.Layer2.Friend")]
#endif
