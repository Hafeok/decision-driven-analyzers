using System;
using Microsoft.CodeAnalysis;

namespace DecisionDriven.Analyzers.Rules;

/// <summary>
/// Whether a type is the framework's.
/// </summary>
/// <remarks>
/// There is no symbol that means "the BCL": it is spread over System.Runtime, System.Collections,
/// System.Memory and a few dozen more, and which one a type lands in is a detail of how the
/// framework was factored rather than something a consumer chose. The core library is matched by
/// identity and the rest by assembly name, which is the same line every tool that needs this line
/// ends up drawing.
/// </remarks>
internal static class Framework
{
    internal static bool Owns(ITypeSymbol type, Compilation compilation)
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
