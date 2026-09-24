using System;
using Microsoft.CodeAnalysis;

namespace DecisionDriven.Analyzers.Rules;

/// <summary>
/// Telling a type that holds a primitive from the shapes a primitive travels in.
/// </summary>
/// <remarks>
/// Shared by DD0013, DD0014 and DD0015, so that "wraps one primitive" means the same thing in the
/// rule that requires the wrapper to be a readonly struct, the rule that lets the wrapper's own
/// members name the primitive, and the rule that stops the wrapper converting back to it silently.
/// </remarks>
internal static class Wrappers
{
    /// <summary>
    /// The shapes a primitive is allowed to arrive in whatever it holds.
    /// </summary>
    /// <remarks>
    /// A span is not a surface saying "a number": it is a window onto memory, and it is what the
    /// parse boundary and the hot paths are made of. Looking inside one would ban the thing the
    /// exemptions exist to allow.
    /// </remarks>
    private static readonly string[] Buffers =
    {
        "System.ReadOnlySpan", "System.Span", "System.Memory", "System.ReadOnlyMemory",
    };

    /// <summary>
    /// The shapes that hand back whatever they hold, so the rule looks through them.
    /// </summary>
    /// <remarks>
    /// <c>Task&lt;string&gt;</c> is a string with waiting attached, and
    /// <c>IReadOnlyList&lt;int&gt;</c> is a surface made of ints. ADR-A09 names the sequences; the
    /// task types are here because a rule that stopped at <c>Task&lt;&gt;</c> would be a rule every
    /// asynchronous surface walked straight past.
    /// </remarks>
    private static readonly string[] Transparent =
    {
        "System.Threading.Tasks.Task", "System.Threading.Tasks.ValueTask",
        "System.Nullable",
        "System.Collections.Generic.IEnumerable",
        "System.Collections.Generic.IReadOnlyList", "System.Collections.Generic.IReadOnlyCollection",
        "System.Collections.Generic.IList", "System.Collections.Generic.ICollection",
    };

    internal static bool IsBuffer(INamedTypeSymbol type) => Matches(type, Buffers);

    internal static bool IsTransparent(INamedTypeSymbol type) =>
        type.TypeArguments.Length == 1 && Matches(type, Transparent);

    /// <summary>
    /// The one banned primitive <paramref name="type"/> wraps, or null when it wraps none or many.
    /// </summary>
    /// <remarks>
    /// Counted in fields rather than properties, because an auto-property is a field with a name in
    /// front of it and a record struct's positional parameter is one too. A type with two fields is
    /// not a wrapper; it is a small model type, and the rule has nothing to say about it.
    /// </remarks>
    internal static ITypeSymbol? Wrapped(INamedTypeSymbol type, BannedPrimitives banned)
    {
        ITypeSymbol? found = null;

        foreach (ISymbol member in type.GetMembers())
        {
            if (member is not IFieldSymbol { IsStatic: false, IsConst: false } field)
            {
                continue;
            }

            if (found is not null)
            {
                return null;
            }

            if (!banned.IsBanned(field.Type))
            {
                return null;
            }

            found = field.Type;
        }

        return found;
    }

    private static bool Matches(INamedTypeSymbol type, string[] names)
    {
        string full = Name(type);

        foreach (string candidate in names)
        {
            if (string.Equals(full, candidate, StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>The type's namespace and name, with no arity and no type arguments.</summary>
    internal static string Name(INamedTypeSymbol type)
    {
        INamedTypeSymbol definition = type.OriginalDefinition;
        string? containing = definition.ContainingNamespace?.ToDisplayString();

        return containing is { Length: > 0 } && containing != "<global namespace>"
            ? containing + "." + definition.Name
            : definition.Name;
    }
}
