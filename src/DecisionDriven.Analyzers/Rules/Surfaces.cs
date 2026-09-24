using System.Collections.Generic;
using System.Threading;
using Microsoft.CodeAnalysis;

namespace DecisionDriven.Analyzers.Rules;

/// <summary>
/// The surfaces ADR-A09 applies to: public members of declared model types, and all contract
/// members.
/// </summary>
/// <remarks>
/// Two surfaces, one reason. A contract is what one package promises another; a public model type
/// is what it promises about the things it hands over. Everything else - parsers, scans, internals -
/// is where primitives belong, and a rule that reached in there would be asking for ceremony rather
/// than for a model.
/// </remarks>
internal static class Surfaces
{
    /// <summary>True when <paramref name="type"/>'s surface is in scope at all.</summary>
    internal static bool Applies(INamedTypeSymbol type, DomainModelNamespaces model) =>
        Markers.Has(type, Markers.Contract) || model.Contains(type);

    /// <summary>
    /// The signature positions to check on <paramref name="type"/>.
    /// </summary>
    /// <remarks>
    /// A contract's members are all in scope, because the whole type is the promise. A model type's
    /// are the ones somebody outside can reach, because the rest is how it is built rather than what
    /// it says.
    /// </remarks>
    internal static IEnumerable<ContractSignature.Part> Parts(
        INamedTypeSymbol type,
        CancellationToken cancellationToken)
    {
        bool contract = Markers.Has(type, Markers.Contract);

        foreach (ContractSignature.Part part in ContractSignature.Parts(type, cancellationToken))
        {
            if (contract || part.IsExternallyVisible)
            {
                yield return part;
            }
        }
    }
}
