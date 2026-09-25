using System.Collections.Generic;
using System.Reflection.Metadata;

namespace DecisionDriven.Report.Metadata;

/// <summary>
/// Full names of types as metadata spells them: namespace, then nested types joined with '+',
/// arity suffixes kept.
/// </summary>
internal static class TypeNames
{
    internal static string Of(MetadataReader reader, TypeDefinitionHandle handle) => Of(reader, handle, '+');

    /// <summary>The name with nested types joined by <paramref name="nesting"/>.</summary>
    internal static string Of(MetadataReader reader, TypeDefinitionHandle handle, char nesting)
    {
        TypeDefinition type = reader.GetTypeDefinition(handle);
        string name = reader.GetString(type.Name);
        TypeDefinitionHandle declaring = type.GetDeclaringType();

        if (!declaring.IsNil)
        {
            return Of(reader, declaring, nesting) + nesting + name;
        }

        string ns = reader.GetString(type.Namespace);
        return ns.Length == 0 ? name : ns + "." + name;
    }

    internal static string Of(MetadataReader reader, TypeReferenceHandle handle) => Of(reader, handle, '+');

    internal static string Of(MetadataReader reader, TypeReferenceHandle handle, char nesting)
    {
        TypeReference type = reader.GetTypeReference(handle);
        string name = reader.GetString(type.Name);

        if (type.ResolutionScope.Kind == HandleKind.TypeReference)
        {
            return Of(reader, (TypeReferenceHandle)type.ResolutionScope, nesting) + nesting + name;
        }

        string ns = reader.GetString(type.Namespace);
        return ns.Length == 0 ? name : ns + "." + name;
    }

    /// <summary>The simple name of the assembly a type reference points into, or null.</summary>
    internal static string? AssemblyOf(MetadataReader reader, TypeReferenceHandle handle)
    {
        TypeReference type = reader.GetTypeReference(handle);

        return type.ResolutionScope.Kind switch
        {
            HandleKind.AssemblyReference =>
                reader.GetString(reader.GetAssemblyReference((AssemblyReferenceHandle)type.ResolutionScope).Name),
            HandleKind.TypeReference => AssemblyOf(reader, (TypeReferenceHandle)type.ResolutionScope),
            _ => null,
        };
    }

    /// <summary>Every type definition, indexed by its full name with '+' nesting.</summary>
    internal static Dictionary<string, TypeDefinitionHandle> Index(MetadataReader reader)
    {
        Dictionary<string, TypeDefinitionHandle> index = new Dictionary<string, TypeDefinitionHandle>(System.StringComparer.Ordinal);

        foreach (TypeDefinitionHandle handle in reader.TypeDefinitions)
        {
            index[Of(reader, handle)] = handle;
        }

        return index;
    }
}
