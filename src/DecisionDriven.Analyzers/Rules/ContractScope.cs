using System;
using Microsoft.CodeAnalysis;

namespace DecisionDriven.Analyzers.Rules;

/// <summary>
/// Which projects the contract rules run in, and which types in them are a package surface.
/// </summary>
/// <remarks>
/// <para>
/// ADR-A08 scopes the contract rules to projects with <c>ArchLayer</c> declared. A project that
/// never declared a layer has not opted into any of this, and reporting its public interfaces would
/// be the package deciding that a consumer has adopted it.
/// </para>
/// <para>
/// Two exclusions. A composition root knows every layer at once by construction
/// (<c>StableDependencyRules.NoServiceLocationOutsideCompositionRoot</c> is the decision that gives it that standing), and
/// a test assembly's public surface is a test list rather than a package surface - the same reason
/// DD0001 and DD0004 leave tests alone.
/// </para>
/// </remarks>
internal static class ContractScope
{
    private const string TestAssemblySuffix = ".Tests";

    /// <summary>True when the contract rules run in this compilation at all.</summary>
    internal static bool Applies(Compilation compilation, ArchOptions options) =>
        options.Layer is not null
        && !options.IsCompositionRoot
        && !(compilation.AssemblyName ?? string.Empty).EndsWith(TestAssemblySuffix, StringComparison.Ordinal);

    /// <summary>
    /// True for the three kinds ADR-A08 calls a contract: a public interface, a public abstract
    /// class, or a public delegate.
    /// </summary>
    /// <remarks>
    /// Public means visible outside the assembly, so a public nested type inside an internal one is
    /// not in scope: nobody outside can name it, so it is not part of any surface.
    /// </remarks>
    internal static bool IsContractShaped(INamedTypeSymbol type) =>
        IsExternallyVisible(type)
        && type.TypeKind switch
        {
            TypeKind.Interface => true,
            TypeKind.Delegate => true,
            TypeKind.Class => type.IsAbstract && !type.IsStatic,
            _ => false,
        };

    private static bool IsExternallyVisible(INamedTypeSymbol type)
    {
        for (INamedTypeSymbol? current = type; current is not null; current = current.ContainingType)
        {
            if (current.DeclaredAccessibility != Accessibility.Public)
            {
                return false;
            }
        }

        return true;
    }
}
