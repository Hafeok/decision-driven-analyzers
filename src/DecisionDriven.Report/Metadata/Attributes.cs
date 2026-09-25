using System;
using System.Collections.Immutable;
using System.Reflection.Metadata;

namespace DecisionDriven.Report.Metadata;

/// <summary>
/// The marker attributes, as metadata carries them.
/// </summary>
/// <remarks>
/// <c>DecisionsAsTypes.AttributesAreSourceGenerated</c>: every assembly has its own internal copy, so
/// they are matched by namespace and name, the same way the analyzers match them.
/// </remarks>
internal static class Attributes
{
    internal const string Namespace = "DecisionDriven";

    internal const string ArchLayer = "ArchLayerAttribute";
    internal const string Contract = "ContractAttribute";
    internal const string DomainModel = "DomainModelAttribute";
    internal const string HotPath = "HotPathAttribute";
    internal const string DesignDecision = "DesignDecisionAttribute";

    /// <summary>The attributes that cite a decision, which is what the projection is about.</summary>
    internal static readonly ImmutableArray<string> Citing =
        ImmutableArray.Create(Contract, DomainModel, HotPath, DesignDecision);

    /// <summary>
    /// The closed set of <c>ExceptionScope</c> values, in declaration order, so an enum argument can
    /// be named. <c>DecisionsAsTypes.ExceptionScopeIsClosed</c>: these are the notations the ledger's
    /// SKOS scheme uses, which is why the projection writes the name rather than the number.
    /// </summary>
    internal static readonly ImmutableArray<string> ExceptionScopes =
        ImmutableArray.Create("Boundary", "HotPath", "Pool", "Interop", "Compatibility", "Migration");

    /// <summary>An attribute decoded far enough to read its arguments.</summary>
    internal readonly struct Decoded
    {
        internal Decoded(string name, CustomAttributeValue<string> value)
        {
            Name = name;
            Value = value;
        }

        /// <summary>The attribute type's name, without its namespace, which is always <see cref="Namespace"/>.</summary>
        internal string Name { get; }

        internal CustomAttributeValue<string> Value { get; }
    }

    /// <summary>
    /// Decodes <paramref name="handle"/> when it is one of the markers named in
    /// <paramref name="wanted"/>, and only then - decoding an arbitrary attribute can fail on an enum
    /// from an assembly that is not here, and there is no reason to try.
    /// </summary>
    internal static Decoded? Read(MetadataReader reader, CustomAttributeHandle handle, ImmutableArray<string> wanted)
    {
        CustomAttribute attribute = reader.GetCustomAttribute(handle);

        if (TypeOf(reader, attribute.Constructor) is not ({ } ns, { } name)
            || ns != Namespace
            || !wanted.Contains(name))
        {
            return null;
        }

        try
        {
            return new Decoded(name, attribute.DecodeValue(TypeNameProvider.Instance));
        }
        catch (BadImageFormatException)
        {
            return null;
        }
    }

    /// <summary>The namespace and name of the type that declares an attribute's constructor.</summary>
    private static (string? Namespace, string? Name) TypeOf(MetadataReader reader, EntityHandle constructor)
    {
        switch (constructor.Kind)
        {
            case HandleKind.MethodDefinition:
                TypeDefinition declaring = reader.GetTypeDefinition(
                    reader.GetMethodDefinition((MethodDefinitionHandle)constructor).GetDeclaringType());
                return (reader.GetString(declaring.Namespace), reader.GetString(declaring.Name));

            case HandleKind.MemberReference:
                EntityHandle parent = reader.GetMemberReference((MemberReferenceHandle)constructor).Parent;

                if (parent.Kind == HandleKind.TypeReference)
                {
                    TypeReference reference = reader.GetTypeReference((TypeReferenceHandle)parent);
                    return (reader.GetString(reference.Namespace), reader.GetString(reference.Name));
                }

                if (parent.Kind == HandleKind.TypeDefinition)
                {
                    TypeDefinition definition = reader.GetTypeDefinition((TypeDefinitionHandle)parent);
                    return (reader.GetString(definition.Namespace), reader.GetString(definition.Name));
                }

                return (null, null);

            default:
                return (null, null);
        }
    }

    /// <summary>
    /// Names types as strings, which is all an attribute argument needs to be read as.
    /// </summary>
    private sealed class TypeNameProvider : ICustomAttributeTypeProvider<string>
    {
        internal static readonly TypeNameProvider Instance = new TypeNameProvider();

        public string GetPrimitiveType(PrimitiveTypeCode typeCode) => typeCode.ToString();

        public string GetSystemType() => "System.Type";

        public string GetSZArrayType(string elementType) => elementType + "[]";

        public string GetTypeFromDefinition(MetadataReader reader, TypeDefinitionHandle handle, byte rawTypeKind) =>
            TypeNames.Of(reader, handle);

        public string GetTypeFromReference(MetadataReader reader, TypeReferenceHandle handle, byte rawTypeKind) =>
            TypeNames.Of(reader, handle);

        public string GetTypeFromSerializedName(string name) => name;

        // The only enum a marker takes is ExceptionScope, which is an int. An enum from somewhere
        // else would be read as one too; nothing here decodes one.
        public PrimitiveTypeCode GetUnderlyingEnumType(string type) => PrimitiveTypeCode.Int32;

        public bool IsSystemType(string type) => string.Equals(type, "System.Type", StringComparison.Ordinal);
    }
}
