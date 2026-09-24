using System;
using System.Collections.Generic;
using Microsoft.CodeAnalysis;

namespace DecisionDriven.Analyzers.Rules;

/// <summary>
/// The types a contract signature is allowed to name.
/// </summary>
/// <remarks>
/// <para>
/// <c>Contracts.ContractVocabularyAllowList</c>. Four sources, and a type from anywhere else is an
/// undecided dependency: the contract is quietly making its consumers depend on a package nobody
/// agreed they should.
/// </para>
/// <para>
/// Built once per compilation, because a contract signature walk touches the same handful of
/// assemblies and namespace prefixes over and over.
/// </para>
/// </remarks>
internal sealed class ContractVocabulary
{
    private readonly Compilation compilation;
    private readonly HashSet<string> listedAssemblies;
    private readonly DomainModelNamespaces domainModel;

    private ContractVocabulary(Compilation compilation, HashSet<string> listedAssemblies, DomainModelNamespaces domainModel)
    {
        this.compilation = compilation;
        this.listedAssemblies = listedAssemblies;
        this.domainModel = domainModel;
    }

    internal static ContractVocabulary Read(Compilation compilation, ArchOptions options)
    {
        HashSet<string> listed = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        // An MSBuild list is semicolon separated; commas are accepted because somebody will write
        // one and a silently ignored assembly name is a rule reporting for a reason nobody can see.
        foreach (string name in (options.ContractTypeAssemblies ?? string.Empty).Split(';', ','))
        {
            string trimmed = name.Trim();
            if (trimmed.Length > 0)
            {
                listed.Add(trimmed);
            }
        }

        return new ContractVocabulary(compilation, listed, DomainModelNamespaces.Read(compilation));
    }

    /// <summary>True when <paramref name="type"/> may appear on a contract signature.</summary>
    internal bool Allows(ITypeSymbol type) => Reason(type) is null;

    /// <summary>
    /// Why <paramref name="type"/> may not appear on a contract signature, or null when it may.
    /// </summary>
    internal string? Reason(ITypeSymbol type)
    {
        // A type parameter is whatever the caller supplies, and the caller's own contract is where
        // that gets decided. An error type is already a compile error.
        if (type.TypeKind is TypeKind.TypeParameter or TypeKind.Error or TypeKind.Dynamic)
        {
            return null;
        }

        if (IsBcl(type))
        {
            return null;
        }

        if (Markers.Has(type, Markers.Contract))
        {
            return null;
        }

        if (type.ContainingAssembly is { } assembly && listedAssemblies.Contains(assembly.Name))
        {
            return null;
        }

        if (domainModel.Contains(type))
        {
            return null;
        }

        string where = type.ContainingAssembly is { } owner
            ? SymbolEqualityComparer.Default.Equals(owner, compilation.Assembly)
                ? "is declared in this assembly outside any [DomainModel] namespace"
                : $"comes from '{owner.Name}', which is not in ArchContractTypeAssemblies"
            : "comes from no assembly this compilation can name";

        return where;
    }

    /// <summary>
    /// The framework, by assembly name.
    /// </summary>
    /// <remarks>
    /// There is no symbol that means "the BCL": it is spread over System.Runtime,
    /// System.Collections, System.Memory and a few dozen more, and which one a type lands in is a
    /// detail of how the framework was factored, not something a consumer chose. The core library
    /// is matched by identity and the rest by name, which is the same test the compiler's own
    /// tooling uses when it has to draw this line.
    /// </remarks>
    internal bool IsFramework(ITypeSymbol type) => IsBcl(type);

    private bool IsBcl(ITypeSymbol type)
    {
        if (type.ContainingAssembly is not { } assembly)
        {
            return false;
        }

        if (SymbolEqualityComparer.Default.Equals(
                assembly,
                compilation.GetSpecialType(SpecialType.System_Object).ContainingAssembly))
        {
            return true;
        }

        string name = assembly.Name;

        return name.Equals("System", StringComparison.Ordinal)
            || name.StartsWith("System.", StringComparison.Ordinal)
            || name.Equals("mscorlib", StringComparison.Ordinal)
            || name.Equals("netstandard", StringComparison.Ordinal);
    }

}
