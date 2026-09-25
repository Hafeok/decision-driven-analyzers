using System;
using System.Collections.Generic;
using System.Reflection.Metadata;

namespace DecisionDriven.Report.Metadata;

/// <summary>
/// Resolving the member a call names to a (contract, member) pair, across assemblies.
/// </summary>
/// <remarks>
/// A call inside the contract's own assembly names a method definition; a call from anywhere else
/// names a member reference whose parent is a type reference, or a type specification when the
/// contract is generic. All three have to land on the same key, or a contract used from outside
/// its assembly would look unused - which is the only use that matters.
/// </remarks>
internal static class Members
{
    /// <summary>A type, by the assembly that defines it and its full name.</summary>
    internal readonly record struct TypeKey(string Assembly, string FullName);

    /// <summary>The type a method definition is declared on.</summary>
    internal static TypeKey DeclaringOf(LoadedAssembly assembly, MethodDefinitionHandle method) =>
        new TypeKey(assembly.Name, TypeNames.Of(assembly.Reader, assembly.Reader.GetMethodDefinition(method).GetDeclaringType()));

    /// <summary>
    /// The type and member name a call's operand names, or null when it is not a method.
    /// </summary>
    internal static (TypeKey Type, string Member)? Called(LoadedAssembly assembly, EntityHandle operand)
    {
        MetadataReader reader = assembly.Reader;

        switch (operand.Kind)
        {
            case HandleKind.MethodDefinition:
                MethodDefinitionHandle definition = (MethodDefinitionHandle)operand;
                return (DeclaringOf(assembly, definition), reader.GetString(reader.GetMethodDefinition(definition).Name));

            case HandleKind.MemberReference:
                MemberReference reference = reader.GetMemberReference((MemberReferenceHandle)operand);
                if (reference.GetKind() != MemberReferenceKind.Method || OwnerOf(assembly, reference.Parent) is not { } owner)
                {
                    return null;
                }

                return (owner, reader.GetString(reference.Name));

            case HandleKind.MethodSpecification:
                return Called(assembly, reader.GetMethodSpecification((MethodSpecificationHandle)operand).Method);

            default:
                return null;
        }
    }

    /// <summary>The type an interface implementation, base type or member parent names.</summary>
    internal static TypeKey? OwnerOf(LoadedAssembly assembly, EntityHandle type)
    {
        MetadataReader reader = assembly.Reader;

        switch (type.Kind)
        {
            case HandleKind.TypeDefinition:
                return new TypeKey(assembly.Name, TypeNames.Of(reader, (TypeDefinitionHandle)type));

            case HandleKind.TypeReference:
                TypeReferenceHandle referenceHandle = (TypeReferenceHandle)type;
                return TypeNames.AssemblyOf(reader, referenceHandle) is { } owner
                    ? new TypeKey(owner, TypeNames.Of(reader, referenceHandle))
                    : null;

            case HandleKind.TypeSpecification:
                return GenericHead(reader, (TypeSpecificationHandle)type) is { } head ? OwnerOf(assembly, head) : null;

            default:
                return null;
        }
    }

    /// <summary>
    /// The member a method belongs to as a reader thinks of it: an accessor is its property or
    /// event, anything else is itself.
    /// </summary>
    internal static string MemberOf(string methodName)
    {
        foreach (string prefix in new[] { "get_", "set_", "add_", "remove_" })
        {
            if (methodName.StartsWith(prefix, StringComparison.Ordinal))
            {
                return methodName.Substring(prefix.Length);
            }
        }

        return methodName;
    }

    /// <summary>The open generic type a <c>Foo&lt;Bar&gt;</c> specification instantiates.</summary>
    private static EntityHandle? GenericHead(MetadataReader reader, TypeSpecificationHandle handle)
    {
        BlobReader blob = reader.GetBlobReader(reader.GetTypeSpecification(handle).Signature);

        if (blob.ReadSignatureTypeCode() != SignatureTypeCode.GenericTypeInstance)
        {
            return null;
        }

        // CLASS or VALUETYPE, then the coded type handle.
        blob.ReadByte();
        return blob.ReadTypeHandle();
    }
}
