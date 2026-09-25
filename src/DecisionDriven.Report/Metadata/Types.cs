using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Metadata;

namespace DecisionDriven.Report.Metadata;

/// <summary>
/// Facts about type definitions that every metric needs the same answer to.
/// </summary>
internal static class Types
{
    /// <summary>
    /// A type somebody wrote, as opposed to one the compiler or a generator put there.
    /// </summary>
    /// <remarks>
    /// Excluded: <c>&lt;Module&gt;</c> and anything whose name starts with <c>&lt;</c>, anything marked
    /// <c>[CompilerGenerated]</c> or <c>[Embedded]</c> (the nullable and ref-safety attributes the
    /// compiler embeds), and the marker attributes and decision types this package's generator emits
    /// into every assembly. Counting those would give every assembly the same handful of extra
    /// types and make abstractness a measure of the generator.
    /// </remarks>
    internal static bool IsWritten(MetadataReader reader, TypeDefinitionHandle handle)
    {
        TypeDefinition type = reader.GetTypeDefinition(handle);
        string name = reader.GetString(type.Name);

        if (name.Length == 0 || name[0] == '<')
        {
            return false;
        }

        string ns = reader.GetString(type.Namespace);
        if (ns == Attributes.Namespace || ns.StartsWith(Attributes.Namespace + ".Ledger", System.StringComparison.Ordinal))
        {
            return false;
        }

        foreach (CustomAttributeHandle attribute in type.GetCustomAttributes())
        {
            if (AttributeName(reader, attribute) is "System.Runtime.CompilerServices.CompilerGeneratedAttribute"
                or "Microsoft.CodeAnalysis.EmbeddedAttribute")
            {
                return false;
            }
        }

        TypeDefinitionHandle declaring = type.GetDeclaringType();
        return declaring.IsNil || IsWritten(reader, declaring);
    }

    internal static bool IsInterface(TypeDefinition type) =>
        (type.Attributes & TypeAttributes.ClassSemanticsMask) == TypeAttributes.Interface;

    /// <summary>An interface, or an abstract class that is not static (static classes are abstract and sealed in metadata).</summary>
    internal static bool IsAbstract(TypeDefinition type) =>
        IsInterface(type)
        || ((type.Attributes & TypeAttributes.Abstract) != 0 && (type.Attributes & TypeAttributes.Sealed) == 0);

    internal static bool IsCompilerGenerated(MetadataReader reader, CustomAttributeHandleCollection attributes)
    {
        foreach (CustomAttributeHandle attribute in attributes)
        {
            if (AttributeName(reader, attribute) == "System.Runtime.CompilerServices.CompilerGeneratedAttribute")
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>True when <paramref name="attributes"/> include the named marker.</summary>
    internal static bool Has(MetadataReader reader, CustomAttributeHandleCollection attributes, string marker)
    {
        foreach (CustomAttributeHandle attribute in attributes)
        {
            if (AttributeName(reader, attribute) == Attributes.Namespace + "." + marker)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>The <c>[DomainModel]</c> namespace prefixes an assembly declares.</summary>
    internal static List<string> DomainModelPrefixes(MetadataReader reader)
    {
        List<string> prefixes = new List<string>();
        System.Collections.Immutable.ImmutableArray<string> wanted =
            System.Collections.Immutable.ImmutableArray.Create(Attributes.DomainModel);

        foreach (CustomAttributeHandle handle in reader.GetAssemblyDefinition().GetCustomAttributes())
        {
            if (Attributes.Read(reader, handle, wanted) is { } decoded
                && decoded.Value.FixedArguments.Length > 0
                && decoded.Value.FixedArguments[0].Value is string prefix
                && prefix.Length > 0)
            {
                prefixes.Add(prefix);
            }
        }

        return prefixes;
    }

    /// <summary>True when <paramref name="ns"/> is one of <paramref name="prefixes"/> or under one.</summary>
    internal static bool InNamespaces(string ns, List<string> prefixes)
    {
        foreach (string prefix in prefixes)
        {
            if (ns == prefix || ns.StartsWith(prefix + ".", System.StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>The declared layer, or null when the assembly has none.</summary>
    internal static int? ArchLayer(MetadataReader reader)
    {
        System.Collections.Immutable.ImmutableArray<string> wanted =
            System.Collections.Immutable.ImmutableArray.Create(Attributes.ArchLayer);

        foreach (CustomAttributeHandle handle in reader.GetAssemblyDefinition().GetCustomAttributes())
        {
            if (Attributes.Read(reader, handle, wanted) is { } decoded
                && decoded.Value.FixedArguments.Length > 0
                && decoded.Value.FixedArguments[0].Value is int layer)
            {
                return layer;
            }
        }

        return null;
    }

    /// <summary>
    /// The type a caller is written in: the outermost type that is not compiler-generated, so a
    /// lambda's closure class is counted as the method that wrote the lambda.
    /// </summary>
    internal static TypeDefinitionHandle Author(MetadataReader reader, TypeDefinitionHandle handle)
    {
        TypeDefinitionHandle current = handle;

        while (true)
        {
            TypeDefinition type = reader.GetTypeDefinition(current);
            string name = reader.GetString(type.Name);
            TypeDefinitionHandle declaring = type.GetDeclaringType();

            if (declaring.IsNil || name.Length == 0 || name[0] != '<')
            {
                return current;
            }

            current = declaring;
        }
    }

    /// <summary>The full name of an attribute's type, or null.</summary>
    private static string? AttributeName(MetadataReader reader, CustomAttributeHandle handle)
    {
        EntityHandle constructor = reader.GetCustomAttribute(handle).Constructor;

        return constructor.Kind switch
        {
            HandleKind.MethodDefinition => TypeNames.Of(
                reader, reader.GetMethodDefinition((MethodDefinitionHandle)constructor).GetDeclaringType()),
            HandleKind.MemberReference => reader.GetMemberReference((MemberReferenceHandle)constructor).Parent switch
            {
                { Kind: HandleKind.TypeReference } parent => TypeNames.Of(reader, (TypeReferenceHandle)parent),
                { Kind: HandleKind.TypeDefinition } parent => TypeNames.Of(reader, (TypeDefinitionHandle)parent),
                _ => null,
            },
            _ => null,
        };
    }
}
