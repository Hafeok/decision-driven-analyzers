using System.Resources;
using System.Runtime.CompilerServices;

// The fixes are authored in English and are not localised.
[assembly: NeutralResourcesLanguage("en")]

// The placeholder text is internal and is asserted verbatim by a test: it is the string ADR-A14
// specifies, and the string a reader greps for.
[assembly: InternalsVisibleTo("DecisionDriven.Analyzers.Tests")]
