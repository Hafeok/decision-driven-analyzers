using System.Collections.Immutable;
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

    /// <summary>Where a rule's doc page lives.</summary>
    private const string HelpLink = "https://github.com/Hafeok/decision-driven-analyzers/blob/main/docs/rules/";

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

    internal static readonly DiagnosticDescriptor DecisionCitation = Rule(
        DiagnosticIds.DecisionCitation,
        "Citation is not a generated decision, or is missing a required argument",
        "{0}. Decide: {1} | file the decision in the ledger, export it, and cite the type the "
            + "generator emits for it. " + Guard,
        "The whole point of a Type argument is that the citation is checked by the compiler. A "
            + "hand-written class of the same shape, or a missing Role or Scope, puts the citation "
            + "back where a string citation was: it parses, and it says nothing.");

    /// <summary>
    /// DD0008 is <see cref="WellKnownDiagnosticTags.NotConfigurable"/>, which is what makes it true
    /// rather than merely stated: a rule that reports suppressions and could itself be suppressed by
    /// one would be a rule with a hole exactly its own size. It also cannot be downgraded in
    /// .editorconfig, which is the third thing it reports.
    /// </summary>
    internal static readonly DiagnosticDescriptor Suppression = new DiagnosticDescriptor(
        id: DiagnosticIds.Suppression,
        title: "Suppression of a DecisionDriven rule",
        messageFormat: "{0}. Decide: {1} | change the rule itself, by superseding the decision that "
            + "set its tier in DecisionDriven.Analyzers. Do not reach for a suppression to get to "
            + "green; if the reason is only that the code already looked like this, take the design "
            + "change.",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        description: "A suppression is the one edit that makes a rule report nothing while the code "
            + "it was about stays exactly as it was. [DesignDecision] costs a filed decision and "
            + "leaves a citation behind; a pragma costs one line and leaves nothing.",
        helpLinkUri: HelpLink + DiagnosticIds.Suppression + ".md",
        customTags: WellKnownDiagnosticTags.NotConfigurable);

    internal static readonly DiagnosticDescriptor ContractDeclaration = Rule(
        DiagnosticIds.ContractDeclaration,
        "Public interface, abstract class or delegate with no cited decision",
        "{0}. Decide: {1} | " + ExceptionPath + ". " + Guard,
        "A public interface is a promise to everyone who can see it, and the promise outlives "
            + "whoever made it. There is no member count at which that becomes true: a one-member "
            + "interface a package exposes is as much a decision as a twenty-member one.");

    /// <summary>
    /// The one rule whose second path is not <see cref="ExceptionPath"/>.
    /// </summary>
    /// <remarks>
    /// Both of DD0010's answers are configuration rather than a citation: say which namespaces are
    /// the model, or say which assemblies a contract may name. The message supplies the actual
    /// namespace and the actual assembly, because "declare [DomainModel]" without saying on what is
    /// the kind of advice that gets read twice and acted on once.
    /// </remarks>
    internal static readonly DiagnosticDescriptor ContractVocabulary = Rule(
        DiagnosticIds.ContractVocabulary,
        "Contract signature names a type from an undecided package",
        "{0}. Decide: {1} | {2}. " + Guard,
        "A type on a contract signature is a dependency every implementer and every caller takes "
            + "on. Nobody agreed to it by agreeing to the contract, and nothing in the reference "
            + "graph shows it as the coupling it is.");

    internal static readonly DiagnosticDescriptor ContractParameter = Rule(
        DiagnosticIds.ContractParameter,
        "Contract parameter is a collaborator rather than data",
        "{0}. Decide: {1} | " + ExceptionPath + ". " + Guard,
        "A collaborator passed in per call is a dependency the caller has to know about and the "
            + "compiler cannot place. Collaborators arrive through a constructor, or through a "
            + "contract that says what they are; parameters are the data a member works on.");

    internal static readonly DiagnosticDescriptor UnhonouredMember = Rule(
        DiagnosticIds.UnhonouredMember,
        "Implemented member throws NotSupportedException",
        "{0}. Decide: {1} | " + ExceptionPath + ". " + Guard,
        "A member that throws rather than works is an interface sized for somebody else's client. "
            + "Every caller now has to know which implementations mean it, which is the knowledge "
            + "the interface existed to remove.");

    internal static readonly DiagnosticDescriptor NakedPrimitive = Rule(
        DiagnosticIds.NakedPrimitive,
        "Naked primitive on a model or contract surface",
        "{0}. Decide: {1} | " + ExceptionPath + ". " + Guard,
        "Two primitives of the same type standing for two different things are two arguments the "
            + "compiler will let a caller swap. The wrapper type is where the difference between "
            + "them is written down, and a surface made of primitives is a surface that never "
            + "wrote it down.");

    internal static readonly DiagnosticDescriptor WrapperShape = Rule(
        DiagnosticIds.WrapperShape,
        "Wrapper around one primitive is not a readonly struct with value equality",
        "{0}. Decide: {1} | " + ExceptionPath + ". " + Guard,
        "A wrapper exists to be as cheap as the primitive it replaces and to compare like it. A "
            + "class allocates on every one; a mutable struct is a value that changes behind its "
            + "holder's back; one without value equality compares by nothing anybody meant.");

    internal static readonly DiagnosticDescriptor ImplicitPrimitiveConversion = Rule(
        DiagnosticIds.ImplicitPrimitiveConversion,
        "Implicit conversion between a model type and a primitive",
        "{0}. Decide: {1} | " + ExceptionPath + ". " + Guard,
        "An implicit conversion puts the swappable argument back while leaving the signature "
            + "looking like it was fixed: the type says Position and the caller may still pass a "
            + "long, or pass a Position where a long was meant.");

    /// <summary>
    /// The one tier-2 rule shipped so far (<c>RuleTiers.ThreeTiers</c>): a warning, because it
    /// reads a declaration and is trying to see a call site it cannot reach. Its false-positive
    /// story is in the doc page and was written before the analyzer was.
    /// </summary>
    internal static readonly DiagnosticDescriptor FlagArgument = Rule(
        DiagnosticIds.FlagArgument,
        "Flag argument on a model or contract member",
        "{0}. Decide: {1} | " + ExceptionPath + ". " + Guard,
        "A bool parameter reads as 'true' at the call site and as its name only in the declaration. "
            + "The second one is worse: two of them are silently swappable, which is the defect "
            + "DD0013 exists to stop.",
        DiagnosticSeverity.Warning);

    /// <summary>
    /// Tier 2 (<c>RuleTiers.ThreeTiers</c>), like DD0016: it reads a switch and is trying to see
    /// every subtype that will ever exist. Its false-positive story is in the doc page.
    /// </summary>
    internal static readonly DiagnosticDescriptor OpenHierarchySwitch = Rule(
        DiagnosticIds.OpenHierarchySwitch,
        "Type switch over a hierarchy nothing closed",
        "{0}. Decide: {1} | " + ExceptionPath + ". " + Guard,
        "A switch over subtypes is a claim that the list is complete. When the base is open the "
            + "claim is not checked by anything, and the next subtype gets whatever the last arm "
            + "does - which is the defect, whether that arm returns a default or throws.",
        DiagnosticSeverity.Warning);

    /// <summary>
    /// The second rule whose second path is not <see cref="ExceptionPath"/>. A citation on a
    /// <c>NotImplementedException</c> would make the placeholder permanent and invisible; the
    /// answer is a different exception, which DD0012 then tracks.
    /// </summary>
    internal static readonly DiagnosticDescriptor NotImplemented = Rule(
        DiagnosticIds.NotImplemented,
        "Placeholder body in non-test code",
        "{0}. Decide: {1} | if it is a deliberate stub, make it a NotSupportedException marked "
            + "[DesignDecision(typeof(<Set>.<Key>), Scope = ExceptionScope.<Scope>)], which DD0012 "
            + "tracks and the report tool lists. "
            + "Do not leave it as it is; a placeholder nobody can find is a placeholder that ships.",
        "NotImplementedException is the one exception type that means nothing about the domain and "
            + "everything about the schedule. It compiles, it passes review, and the only way to "
            + "find it later is to grep for it.");

    internal static readonly DiagnosticDescriptor MutableModel = Rule(
        DiagnosticIds.MutableModel,
        "Model type can be changed by its caller",
        "{0}. Decide: {1} | " + ExceptionPath + ". " + Guard,
        "A value handed out by a pinned read is only trustworthy if the caller cannot change it. "
            + "A settable property, a mutable field or an exposed list means every holder of the "
            + "value shares one, and the snapshot was never a snapshot.");

    /// <summary>
    /// Every descriptor this package ships, so DD0008 can read their declared tiers.
    /// </summary>
    /// <remarks>
    /// A rule added without being added here is a rule whose .editorconfig severity nobody checks,
    /// which is why the test that this list matches SupportedDiagnostics across the assembly exists.
    /// </remarks>
    internal static readonly ImmutableArray<DiagnosticDescriptor> All = ImmutableArray.Create(
        LayerReference,
        InternalsVisibleTo,
        ServiceLocation,
        MutableStaticState,
        BannedName,
        RootNamespace,
        DecisionCitation,
        Suppression,
        ContractDeclaration,
        ContractVocabulary,
        ContractParameter,
        UnhonouredMember,
        NakedPrimitive,
        WrapperShape,
        ImplicitPrimitiveConversion,
        FlagArgument,
        OpenHierarchySwitch,
        NotImplemented,
        MutableModel);

    /// <summary>
    /// One descriptor, at the severity its tier declares.
    /// </summary>
    /// <remarks>
    /// Tier 1 (<c>RuleTiers.ThreeTiers</c>) is mechanical, decidable inside one compilation, and an
    /// error; that is the default because it is what most of these are. A tier-2 rule passes
    /// <see cref="DiagnosticSeverity.Warning"/> and owes a written false-positive story.
    /// </remarks>
    private static DiagnosticDescriptor Rule(
        string id,
        string title,
        string messageFormat,
        string description,
        DiagnosticSeverity severity = DiagnosticSeverity.Error) =>
        new DiagnosticDescriptor(
            id: id,
            title: title,
            messageFormat: messageFormat,
            category: Category,
            defaultSeverity: severity,
            isEnabledByDefault: true,
            description: description,
            helpLinkUri: HelpLink + id + ".md");
}
