using System;
using System.Collections.Concurrent;
using System.Threading;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace DecisionDriven.Analyzers.Rules;

/// <summary>
/// Tells a decision type the generator emitted from anything else that is shaped like one.
/// </summary>
/// <remarks>
/// <para>
/// <c>DecisionsAsTypes.AttributeArgumentsMustBeGenerated</c>. Shape alone is not the test. A type
/// with the right name in the right namespace holding the right three constants is a handful of
/// lines to write by hand, and a citation of one says exactly what a string citation said: that
/// somebody typed something. What makes a citation worth checking is that the type came from the
/// ledger, so provenance is checked too.
/// </para>
/// <para>
/// Provenance is read the way the compiler reads it: a file whose first trivia is an
/// <c>&lt;auto-generated&gt;</c> comment is generated code. That is the convention the generator
/// emits and the one every other tool in the chain already honours. It is not tamper-proof - a
/// consumer who writes that header over a hand-written decision has forged a ledger entry, which is
/// a different problem from the one this rule is for, and not one an analyzer can be the answer to.
/// </para>
/// </remarks>
internal sealed class GeneratedDecisions
{
    /// <summary>The namespace every emitted decision type lives under.</summary>
    internal const string LedgerNamespacePrefix = "DecisionDriven.Ledger.";

    /// <summary>The constant the set class carries.</summary>
    private const string SetIdConstant = "SetId";

    private static readonly string[] DecisionConstants = { "Id", "Key", "Namespace" };

    private readonly ConcurrentDictionary<SyntaxTree, bool> generated =
        new ConcurrentDictionary<SyntaxTree, bool>();

    /// <summary>Why a cited type is not a decision, or <see cref="Verdict.Generated"/> when it is.</summary>
    internal enum Verdict
    {
        /// <summary>A decision type emitted from the ledger.</summary>
        Generated,

        /// <summary>Right shape, wrong provenance: nothing generated this.</summary>
        HandWritten,

        /// <summary>Not a decision type at all.</summary>
        NotADecision,
    }

    /// <summary>Classifies the type a citation names.</summary>
    internal Verdict Classify(INamedTypeSymbol? type, CancellationToken cancellationToken)
    {
        if (type is null || !HasDecisionShape(type))
        {
            return Verdict.NotADecision;
        }

        // No syntax at all means the type came in through metadata rather than being emitted into
        // this compilation. The emitted types are internal, so that only happens across an
        // InternalsVisibleTo grant - which is DD0002's business, and is not a citation this
        // compilation can stand behind either way.
        if (type.DeclaringSyntaxReferences.IsEmpty)
        {
            return Verdict.HandWritten;
        }

        foreach (SyntaxReference reference in type.DeclaringSyntaxReferences)
        {
            if (!IsGeneratedFile(reference.SyntaxTree, cancellationToken))
            {
                return Verdict.HandWritten;
            }
        }

        return Verdict.Generated;
    }

    /// <summary>
    /// A static class nested in a static class, under <c>DecisionDriven.Ledger.*</c>, carrying the
    /// three constants the emitter writes, inside a set class carrying its own.
    /// </summary>
    private static bool HasDecisionShape(INamedTypeSymbol type)
    {
        if (!type.IsStatic || type.TypeKind != TypeKind.Class)
        {
            return false;
        }

        if (type.ContainingType is not { IsStatic: true, TypeKind: TypeKind.Class } set)
        {
            return false;
        }

        if (!HasStringConstant(set, SetIdConstant))
        {
            return false;
        }

        string containing = set.ContainingNamespace?.ToDisplayString() ?? string.Empty;
        if (!containing.StartsWith(LedgerNamespacePrefix, StringComparison.Ordinal))
        {
            return false;
        }

        foreach (string name in DecisionConstants)
        {
            if (!HasStringConstant(type, name))
            {
                return false;
            }
        }

        return true;
    }

    private static bool HasStringConstant(INamedTypeSymbol type, string name)
    {
        foreach (ISymbol member in type.GetMembers(name))
        {
            if (member is IFieldSymbol { IsConst: true, Type.SpecialType: SpecialType.System_String })
            {
                return true;
            }
        }

        return false;
    }

    private bool IsGeneratedFile(SyntaxTree tree, CancellationToken cancellationToken) =>
        generated.GetOrAdd(tree, t => HasGeneratedHeader(t, cancellationToken));

    private static bool HasGeneratedHeader(SyntaxTree tree, CancellationToken cancellationToken)
    {
        SyntaxToken first = tree.GetRoot(cancellationToken).GetFirstToken(includeZeroWidth: true);

        foreach (SyntaxTrivia trivia in first.LeadingTrivia)
        {
            if (!trivia.IsKind(SyntaxKind.SingleLineCommentTrivia)
                && !trivia.IsKind(SyntaxKind.MultiLineCommentTrivia))
            {
                continue;
            }

            if (trivia.ToString().IndexOf("<auto-generated", StringComparison.Ordinal) >= 0)
            {
                return true;
            }
        }

        return false;
    }
}
