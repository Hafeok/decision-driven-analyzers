using System.Collections.Immutable;
using Microsoft.CodeAnalysis;

namespace DecisionDriven.Analyzers.Rules;

/// <summary>
/// The generated marker attributes, as a rule meets them.
/// </summary>
/// <remarks>
/// <c>DecisionsAsTypes.AttributesAreSourceGenerated</c>: they are emitted <c>internal</c> into every
/// compilation, so a rule cannot hold a symbol for "the" attribute and compare by identity. Each
/// assembly has its own, and they are matched by name and namespace, which is the same way the
/// compiler matches <c>[Obsolete]</c> across assemblies that never reference each other.
/// </remarks>
internal static class Markers
{
    /// <summary>The namespace every marker attribute is emitted into.</summary>
    internal const string Namespace = "DecisionDriven";

    internal const string Contract = "ContractAttribute";
    internal const string DomainModel = "DomainModelAttribute";
    internal const string DesignDecision = "DesignDecisionAttribute";

    /// <summary>True when <paramref name="symbol"/> carries the named marker.</summary>
    internal static bool Has(ISymbol symbol, string attributeName)
    {
        foreach (AttributeData attribute in symbol.GetAttributes())
        {
            if (Is(attribute, attributeName))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>The marker attributes on <paramref name="symbol"/> with the given name.</summary>
    internal static ImmutableArray<AttributeData> All(ISymbol symbol, string attributeName)
    {
        ImmutableArray<AttributeData>.Builder builder = ImmutableArray.CreateBuilder<AttributeData>();

        foreach (AttributeData attribute in symbol.GetAttributes())
        {
            if (Is(attribute, attributeName))
            {
                builder.Add(attribute);
            }
        }

        return builder.ToImmutable();
    }

    private static bool Is(AttributeData attribute, string attributeName) =>
        attribute.AttributeClass is { } type
        && type.Name == attributeName
        && type.ContainingNamespace?.ToDisplayString() == Namespace;
}
