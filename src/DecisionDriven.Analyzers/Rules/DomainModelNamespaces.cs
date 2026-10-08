using System;
using System.Collections.Generic;
using Microsoft.CodeAnalysis;

namespace DecisionDriven.Analyzers.Rules;

/// <summary>
/// The namespaces an assembly has declared to be its model.
/// </summary>
/// <remarks>
/// <c>DecisionsAsTypes.AttributesAreSourceGenerated</c> puts these on the assembly as
/// <c>[DomainModel(prefix, decision)]</c>. An assembly that has declared none has not said what its
/// model is, and every rule that asks the question gets the same answer: no.
/// </remarks>
internal sealed class DomainModelNamespaces
{
    private const string IncludeSubNamespaces = "IncludeSubNamespaces";

    private readonly Compilation compilation;
    private readonly List<KeyValuePair<string, bool>> prefixes;

    private DomainModelNamespaces(Compilation compilation, List<KeyValuePair<string, bool>> prefixes)
    {
        this.compilation = compilation;
        this.prefixes = prefixes;
    }

    /// <summary>True when the assembly declared no model at all.</summary>
    internal bool None => prefixes.Count == 0;

    internal static DomainModelNamespaces Read(Compilation compilation)
    {
        List<KeyValuePair<string, bool>> prefixes = new List<KeyValuePair<string, bool>>();

        foreach (AttributeData attribute in Markers.All(compilation.Assembly, Markers.DomainModel))
        {
            if (attribute.ConstructorArguments.Length > 0
                && attribute.ConstructorArguments[0].Value is string prefix
                && prefix.Length > 0)
            {
                prefixes.Add(new KeyValuePair<string, bool>(prefix, IncludesSubNamespaces(attribute)));
            }
        }

        return new DomainModelNamespaces(compilation, prefixes);
    }

    /// <summary>True when <paramref name="type"/> is this assembly's declared model.</summary>
    internal bool Contains(ITypeSymbol type)
    {
        if (type.ContainingAssembly is not { } assembly
            || !SymbolEqualityComparer.Default.Equals(assembly, compilation.Assembly))
        {
            return false;
        }

        if (type.ContainingNamespace?.ToDisplayString() is not { Length: > 0 } containing)
        {
            return false;
        }

        foreach (KeyValuePair<string, bool> declared in prefixes)
        {
            string prefix = declared.Key;

            if (containing.Equals(prefix, StringComparison.Ordinal)
                || (declared.Value
                    && containing.StartsWith(prefix, StringComparison.Ordinal)
                    && containing.Length > prefix.Length
                    && containing[prefix.Length] == '.'))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// <c>DecisionsAsTypes.DomainModelIncludesSubNamespaces</c>: a prefix matches its namespace and
    /// every namespace under it unless the attribute says <c>IncludeSubNamespaces = false</c>, in
    /// which case it matches only the namespace it names.
    /// </summary>
    private static bool IncludesSubNamespaces(AttributeData attribute)
    {
        foreach (KeyValuePair<string, TypedConstant> named in attribute.NamedArguments)
        {
            if (named.Key == IncludeSubNamespaces && named.Value.Value is bool include)
            {
                return include;
            }
        }

        return true;
    }
}
