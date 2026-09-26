using Microsoft.CodeAnalysis;

namespace DecisionDriven.Analyzers.Ledger;

/// <summary>
/// What the generator says when the ledger it was handed does not make sense.
/// </summary>
/// <remarks>
/// <para>
/// These are errors, not warnings. ADR-A07: "a malformed export breaks every project in the
/// repository, which is intended". A generator that shrugged at input it could not read would
/// silently stop emitting the decision types, and every citation in the repository would fail with
/// an unrelated "type or namespace not found" pointing at the citing line rather than at the file
/// that is actually wrong.
/// </para>
/// <para>
/// The DDGEN id family is separate from DD, which is the rules a consumer's code can violate.
/// Nothing a consumer writes in C# can produce one of these; only the ledger input can.
/// </para>
/// </remarks>
internal static class LedgerDiagnostics
{
    /// <summary>
    /// The category these are reported under.
    /// </summary>
    /// <remarks>
    /// The ids are the DDGEN family of <c>RuleTiers.IdFamilies</c>: DD is Roslyn analyzers, DDBUILD
    /// is build-target checks, DDGEN is generator diagnostics. The families are separate so that a
    /// reader can tell from the id alone whether a finding is about the code being compiled or about
    /// the inputs the build was handed. A new family is a new key in that decision.
    /// </remarks>
    private const string Category = "DecisionDriven.Ledger";

    /// <summary>Two decisions claim the same key in one ledger namespace.</summary>
    internal static readonly DiagnosticDescriptor DuplicateKey = new DiagnosticDescriptor(
        id: "DDGEN0001",
        title: "Duplicate decision key in a ledger namespace",
        messageFormat: "Decision key '{0}' is claimed by more than one decision in ledger namespace '{1}' ({2}). "
            + "Decide: give one of them a different key, or merge them if they are the same decision. "
            + "A key identifies a decision across its versions, so two decisions sharing one makes every citation of it ambiguous.",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    /// <summary>A key that cannot be a C# type name.</summary>
    internal static readonly DiagnosticDescriptor InvalidKey = new DiagnosticDescriptor(
        id: "DDGEN0002",
        title: "Decision key does not match the key syntax",
        messageFormat: "Decision key '{0}' in ledger namespace '{1}' does not match " + DecisionKey.Pattern + ". "
            + "Decide: rename the key, or leave the decision uncitable until it has one. "
            + "The key becomes a type name, so a key that is not an identifier cannot be cited from code at all.",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    /// <summary>A version whose key differs from the key of the version it revises.</summary>
    internal static readonly DiagnosticDescriptor KeyChangedBetweenVersions = new DiagnosticDescriptor(
        id: "DDGEN0003",
        title: "Decision key changed between versions",
        messageFormat: "Version '{0}' of decision '{1}' has key '{2}', but the version it revises has key '{3}'. "
            + "Decide: restore the original key, or file the change as a new decision that supersedes this one. "
            + "A key is immutable across versions; changing one silently breaks every citation written against the old key.",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    /// <summary>A key the generator cannot emit because it would collide with a member it emits.</summary>
    /// <remarks>
    /// <c>DecisionsAsTypes.OneTypePerSetNestedTypePerDecision</c> makes a set a static class named by
    /// PascalCasing its id, carrying a <c>SetId</c> constant, and each key a class nested in it. A key
    /// equal to the set's class name is a member named like its enclosing type (CS0542), and a key
    /// equal to <c>SetId</c> is a second member of that name (CS0102). Either fails every consuming
    /// compilation inside generated code the consumer never wrote, so it is reported here, against the
    /// ledger, and the decision is not emitted.
    /// </remarks>
    internal static readonly DiagnosticDescriptor KeyCollidesWithGeneratedMember = new DiagnosticDescriptor(
        id: "DDGEN0005",
        title: "Decision key collides with a member the generator emits",
        messageFormat: "Decision key '{0}' in set '{1}' of ledger namespace '{2}' collides with '{3}', which the generator emits for the set, so the decision cannot be emitted. "
            + "Decide: rename the key, or move the decision to a set whose class name it does not repeat. "
            + "A key becomes a type nested in its set's class, and C# allows neither a member named like its enclosing type nor two members of one name.",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    /// <summary>A line of the N-Triples export that is not a triple.</summary>
    internal static readonly DiagnosticDescriptor UnparseableLine = new DiagnosticDescriptor(
        id: "DDGEN0004",
        title: "Unparseable line in the ledger export",
        messageFormat: "{0}({1}): this is not an N-Triples statement. "
            + "Decide: fix the line, or re-export the ledger rather than editing the export by hand. "
            + "The export is a build input, so a line the generator cannot read is a line whose decision no code can cite.",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true);
}
