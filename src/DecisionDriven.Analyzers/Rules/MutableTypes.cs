using System;
using Microsoft.CodeAnalysis;

namespace DecisionDriven.Analyzers.Rules;

/// <summary>
/// Whether a type can be changed after it is constructed.
/// </summary>
/// <remarks>
/// <para>
/// This is the judgement DD0004 rests on, and it is a heuristic: C# has no way to ask a type
/// whether it is immutable. The bias is towards reporting, because a static of a type that turns
/// out to be immutable is a one-line exemption, and a static of a type that turns out not to be is
/// a concurrency defect nobody will find.
/// </para>
/// <para>
/// The allow list is what ADR-A05 exempts by name: compiled regexes, encodings and the shared
/// pools are famously shared on purpose, and treating them as mutable would mean every project
/// that uses one carries an exemption that says nothing.
/// </para>
/// </remarks>
internal static class MutableTypes
{
    internal static bool IsMutable(ITypeSymbol type)
    {
        if (type is IErrorTypeSymbol)
        {
            return false;
        }

        // An array is a mutable collection whatever it holds.
        if (type is IArrayTypeSymbol)
        {
            return true;
        }

        if (type.SpecialType != SpecialType.None)
        {
            // Primitives, string, object, decimal: none of them can be changed in place. string is
            // the one that matters, and it is the usual static readonly.
            return type.SpecialType == SpecialType.System_Object;
        }

        if (type.TypeKind is TypeKind.Enum or TypeKind.Delegate or TypeKind.TypeParameter)
        {
            return type.TypeKind == TypeKind.Delegate;
        }

        string fullName = type.OriginalDefinition.ToDisplayString();

        if (IsAllowed(fullName))
        {
            return false;
        }

        if (IsKnownMutable(fullName))
        {
            return true;
        }

        // Lazy<T> is exactly as mutable as what it produces.
        if (fullName == "System.Lazy<T>" && type is INamedTypeSymbol { TypeArguments.Length: 1 } lazy)
        {
            return IsMutable(lazy.TypeArguments[0]);
        }

        // An immutable collection is a collection, so the interface test below would report it.
        if (fullName.StartsWith("System.Collections.Immutable.", StringComparison.Ordinal)
            || fullName.StartsWith("System.Collections.Frozen.", StringComparison.Ordinal))
        {
            return false;
        }

        if (ImplementsEnumerable(type))
        {
            return true;
        }

        return HasSettableMember(type);
    }

    /// <summary>
    /// Types ADR-A05 exempts by name.
    /// </summary>
    private static bool IsAllowed(string fullName) => fullName switch
    {
        "System.Text.RegularExpressions.Regex" => true,
        "System.Text.Encoding" => true,
        "System.Buffers.ArrayPool<T>" => true,
        "System.Buffers.MemoryPool<T>" => true,
        "System.DateTime" or "System.DateTimeOffset" or "System.TimeSpan" or "System.Guid" => true,
        "System.DateOnly" or "System.TimeOnly" => true,
        "System.Uri" => true,
        "System.Version" => true,
        "System.Type" => true,
        _ => false,
    };

    private static bool IsKnownMutable(string fullName) => fullName switch
    {
        "System.Text.StringBuilder" => true,
        "System.Threading.AsyncLocal<T>" => true,
        "System.Threading.ThreadLocal<T>" => true,
        _ => false,
    };

    private static bool ImplementsEnumerable(ITypeSymbol type)
    {
        foreach (INamedTypeSymbol @interface in type.AllInterfaces)
        {
            if (@interface.OriginalDefinition.SpecialType == SpecialType.System_Collections_IEnumerable
                || @interface.OriginalDefinition.SpecialType == SpecialType.System_Collections_Generic_IEnumerable_T)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// A publicly settable property or a non-readonly public field: somebody outside can change it,
    /// which is all "mutable" has to mean for a shared static.
    /// </summary>
    private static bool HasSettableMember(ITypeSymbol type)
    {
        for (ITypeSymbol? current = type; current is not null and not IErrorTypeSymbol; current = current.BaseType)
        {
            foreach (ISymbol member in current.GetMembers())
            {
                if (member.DeclaredAccessibility != Accessibility.Public)
                {
                    continue;
                }

                switch (member)
                {
                    case IPropertySymbol { SetMethod: { } setter } when setter.DeclaredAccessibility == Accessibility.Public && !setter.IsInitOnly:
                        return true;
                    case IFieldSymbol { IsReadOnly: false, IsConst: false }:
                        return true;
                }
            }
        }

        return false;
    }
}
