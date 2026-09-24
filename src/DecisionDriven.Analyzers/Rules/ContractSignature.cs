using System.Collections.Generic;
using System.Threading;
using Microsoft.CodeAnalysis;

namespace DecisionDriven.Analyzers.Rules;

/// <summary>
/// What a contract's signatures say, as the parts a rule reports on.
/// </summary>
/// <remarks>
/// Shared by DD0010 and DD0011 so that the two rules disagree about what to allow and never about
/// what a signature is. A delegate's signature is its <c>Invoke</c> method, which is the one place
/// where "the members of the type" is not what a reader means by the signature.
/// </remarks>
internal static class ContractSignature
{
    /// <summary>One place a type appears on a contract.</summary>
    internal readonly struct Part
    {
        internal Part(ITypeSymbol type, string description, string member, Location location, IParameterSymbol? parameter)
        {
            Type = type;
            Description = description;
            Member = member;
            Location = location;
            Parameter = parameter;
        }

        /// <summary>The type written there.</summary>
        internal ITypeSymbol Type { get; }

        /// <summary>How a message names this position: "return type", "parameter 'x'", and so on.</summary>
        internal string Description { get; }

        /// <summary>The member it belongs to.</summary>
        internal string Member { get; }

        internal Location Location { get; }

        /// <summary>Set when this part is a parameter, which is all DD0011 looks at.</summary>
        internal IParameterSymbol? Parameter { get; }
    }

    /// <summary>Every position on <paramref name="type"/>'s public surface where a type is named.</summary>
    internal static IEnumerable<Part> Parts(INamedTypeSymbol type, CancellationToken cancellationToken)
    {
        if (type.TypeKind == TypeKind.Delegate)
        {
            if (type.DelegateInvokeMethod is { } invoke)
            {
                foreach (Part part in FromMethod(invoke, type.Name))
                {
                    yield return part;
                }
            }

            yield break;
        }

        foreach (ISymbol member in type.GetMembers())
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (member.IsImplicitlyDeclared || member.DeclaredAccessibility is Accessibility.Private)
            {
                continue;
            }

            switch (member)
            {
                case IMethodSymbol method when method.MethodKind is MethodKind.Ordinary or MethodKind.Constructor:
                    foreach (Part part in FromMethod(method, method.Name))
                    {
                        yield return part;
                    }

                    break;

                // A property's accessors are methods too, and reporting both the property and its
                // getter would say the same thing twice about one line of source.
                case IPropertySymbol property:
                    yield return new Part(property.Type, "type", property.Name, At(property), null);

                    foreach (IParameterSymbol indexer in property.Parameters)
                    {
                        yield return new Part(indexer.Type, $"parameter '{indexer.Name}'", property.Name, At(indexer), indexer);
                    }

                    break;

                case IEventSymbol @event:
                    yield return new Part(@event.Type, "type", @event.Name, At(@event), null);
                    break;

                case IFieldSymbol field:
                    yield return new Part(field.Type, "type", field.Name, At(field), null);
                    break;
            }
        }
    }

    private static IEnumerable<Part> FromMethod(IMethodSymbol method, string memberName)
    {
        if (!method.ReturnsVoid)
        {
            yield return new Part(method.ReturnType, "return type", memberName, At(method), null);
        }

        foreach (IParameterSymbol parameter in method.Parameters)
        {
            yield return new Part(parameter.Type, $"parameter '{parameter.Name}'", memberName, At(parameter), parameter);
        }
    }

    /// <summary>
    /// Every distinct type a written type mentions: itself, its generic arguments, and through
    /// arrays and pointers to what they are made of.
    /// </summary>
    internal static IEnumerable<ITypeSymbol> Mentioned(ITypeSymbol type)
    {
        switch (type)
        {
            case IArrayTypeSymbol array:
                foreach (ITypeSymbol inner in Mentioned(array.ElementType))
                {
                    yield return inner;
                }

                break;

            case IPointerTypeSymbol pointer:
                foreach (ITypeSymbol inner in Mentioned(pointer.PointedAtType))
                {
                    yield return inner;
                }

                break;

            case INamedTypeSymbol named:
                yield return named;

                foreach (ITypeSymbol argument in named.TypeArguments)
                {
                    foreach (ITypeSymbol inner in Mentioned(argument))
                    {
                        yield return inner;
                    }
                }

                break;

            default:
                yield return type;
                break;
        }
    }

    private static Location At(ISymbol symbol)
    {
        foreach (Location location in symbol.Locations)
        {
            if (location.IsInSource)
            {
                return location;
            }
        }

        return Location.None;
    }
}
