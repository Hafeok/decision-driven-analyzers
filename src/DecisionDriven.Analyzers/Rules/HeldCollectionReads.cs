using System;
using Microsoft.CodeAnalysis;

namespace DecisionDriven.Analyzers.Rules;

/// <summary>
/// Whether a call on a collection a model class holds only reads it.
/// </summary>
/// <remarks>
/// <para>
/// <c>ImmutableModel.DomainModelImmutable</c>: DD0019 reports a method that calls a member of a
/// field DD0004 judges mutable. A <c>HashSet&lt;T&gt;</c> filled in the constructor and only asked
/// <c>Contains</c> afterwards is as immutable as the type gets, and reporting it asked for a
/// decision that said nothing true.
/// </para>
/// <para>
/// A call is a read when the method implements a member of a read-only interface the receiver's
/// type implements, found through the interface map rather than by name, or when it is one of the
/// few reads those interfaces do not cover. Everything else is still a write, including a call
/// through an interface this does not know, so the rule stays conservative.
/// </para>
/// <para>
/// <c>Count</c> and an indexer get are property reads, not invocations, and DD0019 never took
/// them for writes; an indexer set is an assignment and still is one.
/// </para>
/// </remarks>
internal static class HeldCollectionReads
{
    /// <summary>The read-only collection interfaces, by namespace and name without arity.</summary>
    private static readonly string[] ReadOnlyInterfaces =
    {
        "System.Collections.Generic.IReadOnlyCollection",
        "System.Collections.Generic.IReadOnlyList",
        "System.Collections.Generic.IReadOnlyDictionary",
        "System.Collections.Generic.IReadOnlySet",
    };

    /// <summary>
    /// Reads the read-only interfaces do not cover, as (method, receiver types). The method must be
    /// declared in a <c>System.Collections</c> namespace: a type elsewhere with a method of the same
    /// name is not known to read. An empty list of receivers means any; <c>Contains</c> is a read
    /// on a set, and <c>ISet&lt;T&gt;</c> inherits it from <c>ICollection&lt;T&gt;</c>, so it is
    /// the receiver that is named, not the declaring type.
    /// </summary>
    private static readonly (string Method, string[] Receivers)[] NamedReads =
    {
        ("Contains", new[] { "System.Collections.Generic.HashSet", "System.Collections.Generic.ISet" }),
        ("ContainsKey", Array.Empty<string>()),
        ("ContainsValue", Array.Empty<string>()),
        ("IndexOf", Array.Empty<string>()),
        ("GetEnumerator", Array.Empty<string>()),
        ("TryGetValue", Array.Empty<string>()),
    };

    /// <summary>True when <paramref name="called"/>, invoked on a value of <paramref name="receiver"/>, only reads it.</summary>
    internal static bool IsRead(IMethodSymbol called, ITypeSymbol receiver) =>
        ImplementsReadOnlyMember(called, receiver) || IsNamedRead(called, receiver);

    /// <summary>(a): a member of a read-only interface, matched through the receiver's interface map.</summary>
    private static bool ImplementsReadOnlyMember(IMethodSymbol called, ITypeSymbol receiver)
    {
        // A field typed as the read-only interface itself calls the interface's own member.
        if (called.ContainingType is { } declaring && IsReadOnlyInterface(declaring))
        {
            return true;
        }

        foreach (INamedTypeSymbol @interface in receiver.AllInterfaces)
        {
            if (!IsReadOnlyInterface(@interface))
            {
                continue;
            }

            foreach (ISymbol member in @interface.GetMembers())
            {
                if (member is IMethodSymbol method
                    && SymbolEqualityComparer.Default.Equals(receiver.FindImplementationForInterfaceMember(method), called))
                {
                    return true;
                }
            }
        }

        return false;
    }

    /// <summary>(b): a read on the short list, declared by a known collection type.</summary>
    private static bool IsNamedRead(IMethodSymbol called, ITypeSymbol receiver)
    {
        if (called.ContainingType is not { } declaring || !IsCollectionNamespace(declaring))
        {
            return false;
        }

        string? held = receiver is INamedTypeSymbol named ? Wrappers.Name(named) : null;

        foreach ((string method, string[] receivers) in NamedReads)
        {
            if (!string.Equals(called.Name, method, StringComparison.Ordinal))
            {
                continue;
            }

            if (receivers.Length == 0)
            {
                return true;
            }

            foreach (string candidate in receivers)
            {
                if (string.Equals(held, candidate, StringComparison.Ordinal))
                {
                    return true;
                }
            }
        }

        return false;
    }

    private static bool IsReadOnlyInterface(INamedTypeSymbol type)
    {
        string name = Wrappers.Name(type);

        foreach (string candidate in ReadOnlyInterfaces)
        {
            if (string.Equals(name, candidate, StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }

    private static bool IsCollectionNamespace(INamedTypeSymbol type) =>
        type.ContainingNamespace?.ToDisplayString() is "System.Collections" or "System.Collections.Generic" or "System.Collections.ObjectModel";
}
