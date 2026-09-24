namespace DecisionDriven.Analyzers.Rules;

/// <summary>
/// The DD family of <c>RuleTiers.IdFamilies</c>: rules a consumer's own code can violate.
/// </summary>
/// <remarks>
/// Generic rule ids run from DD0001 (<c>TwoPackages.RuleIdPrefixDD</c>). An id is never reused,
/// even after a rule is removed, because a suppression or a doc link written against the old
/// meaning would silently come to mean something else.
/// </remarks>
internal static class DiagnosticIds
{
    /// <summary>A reference that does not point strictly downward through the layers.</summary>
    internal const string LayerReference = "DD0001";

    /// <summary>An <c>InternalsVisibleTo</c> grant to something that is not a test assembly.</summary>
    internal const string InternalsVisibleTo = "DD0002";

    /// <summary>A service resolved at runtime outside the composition root.</summary>
    internal const string ServiceLocation = "DD0003";

    /// <summary>Mutable static state, including the static registry.</summary>
    internal const string MutableStaticState = "DD0004";

    /// <summary>A grab-bag name on an assembly or a namespace.</summary>
    internal const string BannedName = "DD0005";

    /// <summary>A public type outside the assembly's root namespace.</summary>
    internal const string RootNamespace = "DD0006";

    /// <summary>A citation that is not a generated decision type, or is missing a required argument.</summary>
    internal const string DecisionCitation = "DD0007";

    /// <summary>A suppression of a rule in one of this package's id families.</summary>
    internal const string Suppression = "DD0008";

    /// <summary>A public interface, abstract class or delegate with no cited decision.</summary>
    internal const string ContractDeclaration = "DD0009";

    /// <summary>A type on a contract signature that comes from nowhere the contract may name.</summary>
    internal const string ContractVocabulary = "DD0010";

    /// <summary>A collaborator arriving as a contract parameter.</summary>
    internal const string ContractParameter = "DD0011";

    /// <summary>A member claimed by a contract and not honoured.</summary>
    internal const string UnhonouredMember = "DD0012";

    /// <summary>A naked primitive on a model or contract surface.</summary>
    internal const string NakedPrimitive = "DD0013";

    /// <summary>A wrapper around one primitive that is not a readonly struct with value equality.</summary>
    internal const string WrapperShape = "DD0014";

    /// <summary>An implicit conversion between a model type and a primitive.</summary>
    internal const string ImplicitPrimitiveConversion = "DD0015";

    /// <summary>A bool parameter standing in for an enum.</summary>
    internal const string FlagArgument = "DD0016";

    /// <summary>
    /// Every id family this package ships, longest first.
    /// </summary>
    /// <remarks>
    /// <c>RuleTiers.IdFamilies</c>: DD for Roslyn analyzers, DDBUILD for build-target checks, DDGEN
    /// for generator diagnostics. Longest first because DD is a prefix of the other two, and a
    /// membership test that matched DD first would report the family wrong in the message. A
    /// consumer's own product prefix is not in this list and is not knowable here; DD0008 takes it
    /// from <c>dd_rule_id_prefixes</c>.
    /// </remarks>
    internal static readonly string[] IdFamilies = { "DDBUILD", "DDGEN", "DD" };
}
