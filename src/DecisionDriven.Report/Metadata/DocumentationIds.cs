using System;
using System.Collections.Immutable;
using System.Linq;
using System.Reflection.Metadata;
using System.Text;

namespace DecisionDriven.Report.Metadata;

/// <summary>
/// Documentation-comment ids, from metadata.
/// </summary>
/// <remarks>
/// <c>WholeGraphReport.CitationProjectionAsLedgerEntities</c> names a citing symbol by its
/// documentation-comment id - <c>T:Consumer.Catalog.IProductLookup</c> - because that is the one
/// identifier for a symbol that the compiler, the IDE and the XML docs all already agree on, and
/// that survives a file being moved.
/// </remarks>
internal static class DocumentationIds
{
    internal static string Type(MetadataReader reader, TypeDefinitionHandle handle) =>
        "T:" + TypeNames.Of(reader, handle, '.');

    internal static string Method(MetadataReader reader, MethodDefinitionHandle handle)
    {
        MethodDefinition method = reader.GetMethodDefinition(handle);
        string name = MemberName(reader.GetString(method.Name));

        int arity = method.GetGenericParameters().Count;
        if (arity > 0)
        {
            name += "``" + arity.ToString(System.Globalization.CultureInfo.InvariantCulture);
        }

        MethodSignature<string> signature = method.DecodeSignature(Provider.Instance, genericContext: null);

        StringBuilder id = new StringBuilder("M:")
            .Append(TypeNames.Of(reader, method.GetDeclaringType(), '.'))
            .Append('.')
            .Append(name);

        if (signature.ParameterTypes.Length > 0)
        {
            id.Append('(').Append(string.Join(",", signature.ParameterTypes)).Append(')');
        }

        // Conversion operators differ only by return type, so the id carries it.
        string raw = reader.GetString(method.Name);
        if (raw is "op_Implicit" or "op_Explicit")
        {
            id.Append('~').Append(signature.ReturnType);
        }

        return id.ToString();
    }

    internal static string Property(MetadataReader reader, TypeDefinitionHandle declaring, PropertyDefinitionHandle handle)
    {
        PropertyDefinition property = reader.GetPropertyDefinition(handle);
        MethodSignature<string> signature = property.DecodeSignature(Provider.Instance, genericContext: null);

        string id = "P:" + TypeNames.Of(reader, declaring, '.') + "." + MemberName(reader.GetString(property.Name));

        return signature.ParameterTypes.Length == 0
            ? id
            : id + "(" + string.Join(",", signature.ParameterTypes) + ")";
    }

    internal static string Field(MetadataReader reader, TypeDefinitionHandle declaring, FieldDefinitionHandle handle) =>
        "F:" + TypeNames.Of(reader, declaring, '.') + "." + MemberName(reader.GetString(reader.GetFieldDefinition(handle).Name));

    internal static string Event(MetadataReader reader, TypeDefinitionHandle declaring, EventDefinitionHandle handle) =>
        "E:" + TypeNames.Of(reader, declaring, '.') + "." + MemberName(reader.GetString(reader.GetEventDefinition(handle).Name));

    /// <summary>A namespace, which is what a <c>[DomainModel]</c> citation is about.</summary>
    internal static string Namespace(string name) => "N:" + name;

    /// <summary>
    /// A member name as a documentation id spells it: constructors as <c>#ctor</c>, and the dots in
    /// an explicit interface implementation's name as <c>#</c>.
    /// </summary>
    private static string MemberName(string name) => name switch
    {
        ".ctor" => "#ctor",
        ".cctor" => "#cctor",
        _ => name.Replace('.', '#'),
    };

    /// <summary>Parameter types in documentation-id form.</summary>
    private sealed class Provider : ISignatureTypeProvider<string, object?>
    {
        internal static readonly Provider Instance = new Provider();

        public string GetPrimitiveType(PrimitiveTypeCode typeCode) => typeCode switch
        {
            PrimitiveTypeCode.Boolean => "System.Boolean",
            PrimitiveTypeCode.Byte => "System.Byte",
            PrimitiveTypeCode.SByte => "System.SByte",
            PrimitiveTypeCode.Char => "System.Char",
            PrimitiveTypeCode.Int16 => "System.Int16",
            PrimitiveTypeCode.UInt16 => "System.UInt16",
            PrimitiveTypeCode.Int32 => "System.Int32",
            PrimitiveTypeCode.UInt32 => "System.UInt32",
            PrimitiveTypeCode.Int64 => "System.Int64",
            PrimitiveTypeCode.UInt64 => "System.UInt64",
            PrimitiveTypeCode.Single => "System.Single",
            PrimitiveTypeCode.Double => "System.Double",
            PrimitiveTypeCode.IntPtr => "System.IntPtr",
            PrimitiveTypeCode.UIntPtr => "System.UIntPtr",
            PrimitiveTypeCode.Object => "System.Object",
            PrimitiveTypeCode.String => "System.String",
            PrimitiveTypeCode.TypedReference => "System.TypedReference",
            _ => "System.Void",
        };

        public string GetTypeFromDefinition(MetadataReader reader, TypeDefinitionHandle handle, byte rawTypeKind) =>
            TypeNames.Of(reader, handle, '.');

        public string GetTypeFromReference(MetadataReader reader, TypeReferenceHandle handle, byte rawTypeKind) =>
            TypeNames.Of(reader, handle, '.');

        public string GetTypeFromSpecification(MetadataReader reader, object? genericContext, TypeSpecificationHandle handle, byte rawTypeKind) =>
            reader.GetTypeSpecification(handle).DecodeSignature(this, genericContext);

        public string GetGenericInstantiation(string genericType, ImmutableArray<string> typeArguments)
        {
            int tick = genericType.LastIndexOf('`');
            string open = tick < 0 ? genericType : genericType.Substring(0, tick);
            return open + "{" + string.Join(",", typeArguments) + "}";
        }

        public string GetSZArrayType(string elementType) => elementType + "[]";

        public string GetArrayType(string elementType, ArrayShape shape) =>
            elementType + "[" + string.Join(",", Enumerable.Repeat("0:", shape.Rank)) + "]";

        public string GetByReferenceType(string elementType) => elementType + "@";

        public string GetPointerType(string elementType) => elementType + "*";

        public string GetPinnedType(string elementType) => elementType;

        public string GetModifiedType(string modifier, string unmodifiedType, bool isRequired) => unmodifiedType;

        public string GetGenericTypeParameter(object? genericContext, int index) =>
            "`" + index.ToString(System.Globalization.CultureInfo.InvariantCulture);

        public string GetGenericMethodParameter(object? genericContext, int index) =>
            "``" + index.ToString(System.Globalization.CultureInfo.InvariantCulture);

        public string GetFunctionPointerType(MethodSignature<string> signature) => "System.IntPtr";
    }
}
