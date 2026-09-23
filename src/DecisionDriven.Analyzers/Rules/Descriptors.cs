using Microsoft.CodeAnalysis;

namespace DecisionDriven.Analyzers.Rules;

/// <summary>
/// The rules' diagnostic descriptors.
/// </summary>
/// <remarks>
/// <para>
/// Every message follows <c>DiagnosticMessages.MessageAsksTheDecisionQuestion</c>:
/// </para>
/// <code>
/// &lt;what was found&gt;. Decide: &lt;design-change path&gt; | &lt;documented-exception path&gt;. &lt;guard&gt;
/// </code>
/// <para>
/// The messages are long by analyzer standards and that is the point. A description and a help link
/// are IDE-only; somebody reading <c>dotnet build</c> output, or an agent reading it, sees the id
/// and the message and nothing else. If the message said only what was wrong, the shortest path to
/// green would be to add the attribute it mentions, which is the laundering the guard sentence
/// exists to stop.
/// </para>
/// </remarks>
internal static class Descriptors
{
    /// <summary>Every DD rule reports under this category.</summary>
    internal const string Category = "DecisionDriven";

    /// <summary>
    /// The sentence that ends every message.
    /// </summary>
    internal const string Guard =
        "Do not add the attribute without a decision that answers this; "
        + "if the reason is only that the code already looked like this, take the design change.";

    /// <summary>
    /// The exception path. Named the same way everywhere, because it is the same act everywhere:
    /// cite an accepted decision that says this violation is intended.
    /// </summary>
    internal const string ExceptionPath =
        "mark it [DesignDecision(typeof(<Set>.<Key>), Scope = ExceptionScope.<Scope>)] "
        + "citing the accepted decision that says so";

    internal static readonly DiagnosticDescriptor LayerReference = Rule(
        DiagnosticIds.LayerReference,
        "Reference does not point strictly downward",
        "{0}. Decide: {1} | " + ExceptionPath + ". " + Guard,
        "A project references only projects in strictly lower layers of its own family. A reference "
            + "that points sideways or upward is a cycle waiting to be written, and one that points at "
            + "a family assembly with no declared layer is a project nobody has placed.");

    internal static readonly DiagnosticDescriptor InternalsVisibleTo = Rule(
        DiagnosticIds.InternalsVisibleTo,
        "InternalsVisibleTo grants access to something that is not a test assembly",
        "{0}. Decide: {1} | " + ExceptionPath + ". " + Guard,
        "InternalsVisibleTo to a non-test assembly is a dependency that no reference graph shows and "
            + "no layering rule can see. Tests are the one case where reaching inside is the point.");

    internal static readonly DiagnosticDescriptor ServiceLocation = Rule(
        DiagnosticIds.ServiceLocation,
        "Service resolved at runtime outside the composition root",
        "{0}. Decide: {1} | " + ExceptionPath + ". " + Guard,
        "Resolving a service from a container turns a dependency the compiler could have checked into "
            + "one that fails at run time, and hides it from every rule that reads the reference graph. "
            + "The composition root is where wiring belongs, and it says so with ArchCompositionRoot.");

    internal static readonly DiagnosticDescriptor MutableStaticState = Rule(
        DiagnosticIds.MutableStaticState,
        "Mutable static state",
        "{0}. Decide: {1} | " + ExceptionPath + ". " + Guard,
        "A mutable static is shared by every caller on every thread, and a static collection is how "
            + "a lower layer discovers a higher one without a reference - the hole a layering rule "
            + "cannot see. Pools and interning caches are the honest exception, and they say so.");

    internal static readonly DiagnosticDescriptor BannedName = Rule(
        DiagnosticIds.BannedName,
        "Grab-bag name on an assembly or namespace",
        "{0}. Decide: {1} | " + ExceptionPath + ". " + Guard,
        "Common, Core, Utils and their relatives are where the second reason to change accumulates. "
            + "The name is the only honest cohesion gate: size metrics measure size, not responsibility.");

    internal static readonly DiagnosticDescriptor RootNamespace = Rule(
        DiagnosticIds.RootNamespace,
        "Public type outside the assembly's root namespace",
        "{0}. Decide: {1} | " + ExceptionPath + ". " + Guard,
        "An assembly with one root namespace equal to its name is one package with one name. Public "
            + "types outside it are a second package hiding in the first.");

    private static DiagnosticDescriptor Rule(string id, string title, string messageFormat, string description) =>
        new DiagnosticDescriptor(
            id: id,
            title: title,
            messageFormat: messageFormat,
            category: Category,
            // Tier 1 (RuleTiers.ThreeTiers): mechanical, decidable inside one compilation, error.
            defaultSeverity: DiagnosticSeverity.Error,
            isEnabledByDefault: true,
            description: description,
            helpLinkUri: "https://github.com/Hafeok/decision-driven-analyzers/blob/main/docs/rules/" + id + ".md");
}
