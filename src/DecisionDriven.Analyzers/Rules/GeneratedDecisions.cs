using System;
using DecisionDriven.Analyzers.Generation;
using Microsoft.CodeAnalysis;

namespace DecisionDriven.Analyzers.Rules;

/// <summary>
/// Tells a decision type the generator emitted from anything else that is shaped like one.
/// </summary>
/// <remarks>
/// <para>
/// <c>DecisionsAsTypes.AttributeArgumentsMustBeGenerated</c>. Shape alone is not the test. A type
/// with the right name in the right namespace holding the right three constants is a handful of
/// lines to write by hand, and a citation of one says exactly what a string citation said: that
/// somebody typed something. What makes a citation worth checking is that the type came out of a
/// generator run, so that is what is checked.
/// </para>
/// <para>
/// Two signals, both required. The type carries
/// <c>[System.CodeDom.Compiler.GeneratedCode("DecisionDriven.Analyzers", …)]</c>, and its declaring
/// tree has the synthetic path Roslyn gives generator output. The attribute alone is text anyone
/// can type; the path alone would accept a file that no longer claims to be generated. Together the
/// only way past them is to forge a compiler input, which is the right place for the edge this rule
/// cannot decide to sit - well outside anything a source file can do.
/// </para>
/// </remarks>
internal static class GeneratedDecisions
{
    /// <summary>The namespace every emitted decision type lives under.</summary>
    internal const string LedgerNamespacePrefix = "DecisionDriven.Ledger.";

    /// <summary>The constant the set class carries.</summary>
    private const string SetIdConstant = "SetId";

    private static readonly string[] DecisionConstants = { "Id", "Key", "Namespace" };

    /// <summary>Why a cited type is not a decision, or <see cref="Verdict.Generated"/> when it is.</summary>
    internal enum Verdict
    {
        /// <summary>A decision type this generator emitted.</summary>
        Generated,

        /// <summary>Right shape, and no generator run behind it.</summary>
        HandWritten,

        /// <summary>Not a decision type at all.</summary>
        NotADecision,
    }

    /// <summary>Classifies the type a citation names.</summary>
    internal static Verdict Classify(INamedTypeSymbol? type)
    {
        if (type is null || !HasDecisionShape(type))
        {
            return Verdict.NotADecision;
        }

        if (!CarriesGeneratedCode(type))
        {
            return Verdict.HandWritten;
        }

        // No syntax at all means the type arrived through metadata rather than being emitted into
        // this compilation. The emitted types are internal, so that only happens across an
        // InternalsVisibleTo grant - which is DD0002's business, and is not a citation this
        // compilation can stand behind either way.
        if (type.DeclaringSyntaxReferences.IsEmpty)
        {
            return Verdict.HandWritten;
        }

        foreach (SyntaxReference reference in type.DeclaringSyntaxReferences)
        {
            if (!GeneratorIdentity.IsGeneratedTreePath(reference.SyntaxTree.FilePath))
            {
                return Verdict.HandWritten;
            }
        }

        return Verdict.Generated;
    }

    /// <summary>The BCL marker, naming this generator as the tool.</summary>
    /// <remarks>
    /// The version argument is not checked. It records which version emitted the file, which is
    /// worth having in the source and is not a thing a citation can be right or wrong about.
    /// </remarks>
    private static bool CarriesGeneratedCode(INamedTypeSymbol type)
    {
        foreach (AttributeData attribute in type.GetAttributes())
        {
            if (attribute.AttributeClass?.ToDisplayString() != GeneratorIdentity.GeneratedCodeAttribute)
            {
                continue;
            }

            if (attribute.ConstructorArguments.Length > 0
                && attribute.ConstructorArguments[0].Value is string tool
                && string.Equals(tool, GeneratorIdentity.ToolName, StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
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
}
