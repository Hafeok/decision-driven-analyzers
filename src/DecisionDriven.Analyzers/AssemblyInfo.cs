// Assembly-level attributes for the analyzer assembly.
//
// There is no code in this project yet: this repository's bootstrap builds the package,
// and the rules arrive afterwards. What is here is what an analyzer assembly needs to be
// well-formed regardless of which rules it ends up carrying.

using System.Resources;
using System.Runtime.CompilerServices;

// The diagnostics this assembly produces are authored in English and are not localised.
// Saying so lets the resource lookup skip the satellite-assembly probe it would otherwise
// do on every message.
[assembly: NeutralResourcesLanguage("en")]

// The tests exercise the analyzers, including the parts that are not part of the package's
// public surface. Analyzers are found by the compiler through their attributes rather than
// through a public API, so almost everything here stays internal.
[assembly: InternalsVisibleTo("DecisionDriven.Analyzers.Tests")]
