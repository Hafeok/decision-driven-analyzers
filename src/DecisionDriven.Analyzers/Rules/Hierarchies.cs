using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading;
using Microsoft.CodeAnalysis;

namespace DecisionDriven.Analyzers.Rules;

/// <summary>
/// What a set of subtypes has in common, and whether anything closed it.
/// </summary>
/// <remarks>
/// <c>Hierarchies.ClosedHierarchiesAreSealed</c>. A hierarchy is closed when nobody outside can add
/// to it, and C# has two ways of saying that: a constructor nobody outside can call, and a file-local
/// type nobody outside the file can see. Everything else is a convention, which is the thing DD0017
/// exists because conventions do not hold.
/// </remarks>
internal sealed class Hierarchies
{
    private readonly Compilation compilation;
    private readonly ConcurrentDictionary<INamedTypeSymbol, bool> closed =
        new ConcurrentDictionary<INamedTypeSymbol, bool>(SymbolEqualityComparer.Default);

    private List<INamedTypeSymbol>? declared;

    internal Hierarchies(Compilation compilation)
    {
        this.compilation = compilation;
    }

    /// <summary>
    /// The nearest base class every tested type shares, or null when the only one is object.
    /// </summary>
    /// <remarks>
    /// Interfaces are deliberately not considered. An interface is open by construction - anybody
    /// can implement one - so every switch over interfaces would report, and the rule would be
    /// saying "do not switch on interfaces" rather than what it means.
    /// </remarks>
    internal INamedTypeSymbol? CommonBase(List<INamedTypeSymbol> types)
    {
        INamedTypeSymbol? common = null;

        foreach (INamedTypeSymbol type in types)
        {
            if (common is null)
            {
                common = type;
                continue;
            }

            common = Nearest(common, type);

            if (common is null || common.SpecialType == SpecialType.System_Object)
            {
                return null;
            }
        }

        // The base has to be a base: two arms testing one type, or a type and itself, is not a
        // claim about a set of subtypes.
        return common is null || common.SpecialType == SpecialType.System_Object || Same(common, types)
            ? null
            : common;
    }

    /// <summary>True when nothing outside this assembly can add a subtype.</summary>
    internal bool IsClosed(INamedTypeSymbol type, CancellationToken cancellationToken) =>
        closed.GetOrAdd(type, t => Compute(t, cancellationToken));

    private bool Compute(INamedTypeSymbol type, CancellationToken cancellationToken)
    {
        if (type.IsSealed || type.IsFileLocal)
        {
            return true;
        }

        bool anyConstructor = false;
        bool derivableOutside = false;
        bool allPrivate = true;

        foreach (IMethodSymbol constructor in type.InstanceConstructors)
        {
            if (IsRecordCopyConstructor(type, constructor))
            {
                continue;
            }

            anyConstructor = true;

            if (constructor.DeclaredAccessibility is Accessibility.Public or Accessibility.Protected
                or Accessibility.ProtectedOrInternal)
            {
                derivableOutside = true;
            }

            if (constructor.DeclaredAccessibility != Accessibility.Private)
            {
                allPrivate = false;
            }
        }

        if (!anyConstructor)
        {
            return false;
        }

        // A private constructor closes it outright: only nested types can call one, and they are in
        // this file.
        if (allPrivate)
        {
            return true;
        }

        // A public constructor only opens the hierarchy to somebody who can name the type. An
        // internal base with an implicit public constructor is not derivable outside the assembly,
        // which is the half of ADR-A10's closed condition this was missing: it warned on an
        // internal base with every leaf sealed.
        if (derivableOutside && IsExternallyVisible(type))
        {
            return false;
        }

        // An internal or private-protected constructor closes it only as far as this assembly, so
        // the remaining question is whether this assembly has left a leaf open.
        return AllDerivedAreSealed(type, cancellationToken);
    }

    /// <summary>
    /// A record's copy constructor, which does not decide whether the hierarchy is closed.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <c>Hierarchies.ClosedHierarchiesAreSealed</c>, as amended. The compiler gives every non-sealed
    /// record a <c>protected</c> copy constructor, <c>R(R original)</c>, and forbids declaring it any
    /// narrower (CS8878). Counting it would make every abstract record base open whatever its author
    /// wrote, and the design-change half of DD0017's message would have no path to take.
    /// </para>
    /// <para>
    /// It is a real, narrow door and this says so rather than pretending otherwise: a record in
    /// another assembly can derive by passing an existing instance of the hierarchy to that
    /// constructor. Doing so is deliberate - it needs an instance of a subtype this assembly made,
    /// handed to a record written to reach past the base's other constructors - and no declaration
    /// can prevent it, so it is not treated as the base being open.
    /// </para>
    /// </remarks>
    private static bool IsRecordCopyConstructor(INamedTypeSymbol type, IMethodSymbol constructor) =>
        type.IsRecord
        && constructor.Parameters.Length == 1
        && SymbolEqualityComparer.Default.Equals(constructor.Parameters[0].Type, type);

    private bool AllDerivedAreSealed(INamedTypeSymbol type, CancellationToken cancellationToken)
    {
        foreach (INamedTypeSymbol candidate in Declared(cancellationToken))
        {
            if (SymbolEqualityComparer.Default.Equals(candidate, type))
            {
                continue;
            }

            if (DerivesFrom(candidate, type) && !candidate.IsSealed)
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>Every named type this assembly declares, walked once.</summary>
    private List<INamedTypeSymbol> Declared(CancellationToken cancellationToken)
    {
        if (declared is not null)
        {
            return declared;
        }

        List<INamedTypeSymbol> found = new List<INamedTypeSymbol>();
        Stack<INamespaceOrTypeSymbol> pending = new Stack<INamespaceOrTypeSymbol>();
        pending.Push(compilation.Assembly.GlobalNamespace);

        while (pending.Count > 0)
        {
            cancellationToken.ThrowIfCancellationRequested();
            INamespaceOrTypeSymbol current = pending.Pop();

            foreach (ISymbol member in current.GetMembers())
            {
                switch (member)
                {
                    case INamespaceSymbol child:
                        pending.Push(child);
                        break;

                    case INamedTypeSymbol type:
                        found.Add(type);
                        pending.Push(type);
                        break;
                }
            }
        }

        return declared = found;
    }

    private static bool IsExternallyVisible(INamedTypeSymbol type)
    {
        for (INamedTypeSymbol? current = type; current is not null; current = current.ContainingType)
        {
            if (current.DeclaredAccessibility is not (Accessibility.Public or Accessibility.Protected
                or Accessibility.ProtectedOrInternal))
            {
                return false;
            }
        }

        return true;
    }

    private static bool DerivesFrom(INamedTypeSymbol type, INamedTypeSymbol baseType)
    {
        for (INamedTypeSymbol? current = type.BaseType; current is not null; current = current.BaseType)
        {
            if (SymbolEqualityComparer.Default.Equals(current.OriginalDefinition, baseType.OriginalDefinition))
            {
                return true;
            }
        }

        return false;
    }

    private static INamedTypeSymbol? Nearest(INamedTypeSymbol left, INamedTypeSymbol right)
    {
        for (INamedTypeSymbol? candidate = left; candidate is not null; candidate = candidate.BaseType)
        {
            for (INamedTypeSymbol? other = right; other is not null; other = other.BaseType)
            {
                if (SymbolEqualityComparer.Default.Equals(candidate.OriginalDefinition, other.OriginalDefinition))
                {
                    return candidate;
                }
            }
        }

        return null;
    }

    private static bool Same(INamedTypeSymbol common, List<INamedTypeSymbol> types)
    {
        foreach (INamedTypeSymbol type in types)
        {
            if (SymbolEqualityComparer.Default.Equals(common, type))
            {
                return true;
            }
        }

        return false;
    }
}
