using System;
using System.Collections.Generic;
using System.Reflection.Metadata;
using DecisionDriven.Report.Metadata;

namespace DecisionDriven.Report.Citations;

/// <summary>
/// Every symbol that cites a decision, read out of the built assemblies.
/// </summary>
/// <remarks>
/// <para>
/// The cited decision is read from the generated type the attribute names, not from its name: the
/// generator writes the decision's ledger id, key and namespace into that type as constants, so
/// the projection uses exactly the identity the compiler checked rather than re-deriving it from a
/// PascalCased set name.
/// </para>
/// <para>
/// The attribute's type argument is serialized in metadata as a type name, and a nested type is
/// spelled with <c>+</c> - which is what the index this reads from is keyed on.
/// </para>
/// </remarks>
internal static class CitationFinder
{
    internal static List<Citation> Find(IReadOnlyList<LoadedAssembly> assemblies)
    {
        List<Citation> citations = new List<Citation>();

        foreach (LoadedAssembly assembly in assemblies)
        {
            MetadataReader reader = assembly.Reader;
            Dictionary<string, TypeDefinitionHandle> types = TypeNames.Index(reader);

            foreach (CustomAttributeHandle attribute in reader.GetAssemblyDefinition().GetCustomAttributes())
            {
                Add(citations, assembly, types, attribute, symbol: null, sourceFile: null);
            }

            foreach (TypeDefinitionHandle typeHandle in reader.TypeDefinitions)
            {
                TypeDefinition type = reader.GetTypeDefinition(typeHandle);
                string? typeFile = null;
                bool typeFileRead = false;

                string? TypeFile()
                {
                    if (!typeFileRead)
                    {
                        typeFile = Sources.OfType(assembly, typeHandle);
                        typeFileRead = true;
                    }

                    return typeFile;
                }

                foreach (CustomAttributeHandle attribute in type.GetCustomAttributes())
                {
                    Add(citations, assembly, types, attribute, DocumentationIds.Type(reader, typeHandle), TypeFile);
                }

                foreach (MethodDefinitionHandle method in type.GetMethods())
                {
                    foreach (CustomAttributeHandle attribute in reader.GetMethodDefinition(method).GetCustomAttributes())
                    {
                        Add(citations, assembly, types, attribute, DocumentationIds.Method(reader, method),
                            () => Sources.OfMethod(assembly, method) ?? TypeFile());
                    }
                }

                foreach (PropertyDefinitionHandle property in type.GetProperties())
                {
                    PropertyDefinition definition = reader.GetPropertyDefinition(property);
                    PropertyAccessors accessors = definition.GetAccessors();
                    MethodDefinitionHandle accessor = accessors.Getter.IsNil ? accessors.Setter : accessors.Getter;

                    foreach (CustomAttributeHandle attribute in definition.GetCustomAttributes())
                    {
                        Add(citations, assembly, types, attribute, DocumentationIds.Property(reader, typeHandle, property),
                            () => (accessor.IsNil ? null : Sources.OfMethod(assembly, accessor)) ?? TypeFile());
                    }
                }

                foreach (FieldDefinitionHandle field in type.GetFields())
                {
                    foreach (CustomAttributeHandle attribute in reader.GetFieldDefinition(field).GetCustomAttributes())
                    {
                        Add(citations, assembly, types, attribute, DocumentationIds.Field(reader, typeHandle, field), TypeFile);
                    }
                }

                foreach (EventDefinitionHandle @event in type.GetEvents())
                {
                    foreach (CustomAttributeHandle attribute in reader.GetEventDefinition(@event).GetCustomAttributes())
                    {
                        Add(citations, assembly, types, attribute, DocumentationIds.Event(reader, typeHandle, @event), TypeFile);
                    }
                }
            }
        }

        return citations;
    }

    /// <summary>Adds the citation <paramref name="handle"/> makes, if it is a citing marker.</summary>
    /// <remarks>
    /// <paramref name="symbol"/> is null for an assembly-level attribute, which only
    /// <c>[DomainModel]</c> is, and whose symbol is the namespace it names. The source file is a
    /// function so that the PDB is only read for attributes that turn out to be citations.
    /// </remarks>
    private static void Add(
        List<Citation> citations,
        LoadedAssembly assembly,
        Dictionary<string, TypeDefinitionHandle> types,
        CustomAttributeHandle handle,
        string? symbol,
        Func<string?>? sourceFile)
    {
        MetadataReader reader = assembly.Reader;

        if (Attributes.Read(reader, handle, Attributes.Citing) is not { } decoded)
        {
            return;
        }

        // [DomainModel(prefix, decision)] takes the decision second; the other three take it first.
        bool domainModel = decoded.Name == Attributes.DomainModel;
        int position = domainModel ? 1 : 0;

        if (decoded.Value.FixedArguments.Length <= position
            || decoded.Value.FixedArguments[position].Value is not string typeName)
        {
            return;
        }

        if (domainModel)
        {
            if (decoded.Value.FixedArguments[0].Value is not string prefix)
            {
                return;
            }

            symbol = DocumentationIds.Namespace(prefix);
        }

        if (symbol is null || Decision(reader, types, typeName) is not ({ } id, { } ns, { } key))
        {
            return;
        }

        string? scope = null;
        foreach (CustomAttributeNamedArgument<string> named in decoded.Value.NamedArguments)
        {
            if (named.Name == "Scope" && named.Value is int value && value >= 0 && value < Attributes.ExceptionScopes.Length)
            {
                scope = Attributes.ExceptionScopes[value];
            }
        }

        string attributeName = decoded.Name.Substring(0, decoded.Name.Length - "Attribute".Length);

        citations.Add(new Citation(assembly.Name, symbol, attributeName, id, ns, key, scope, sourceFile?.Invoke()));
    }

    /// <summary>The ledger id, namespace and key the generated decision type carries as constants.</summary>
    private static (string? Id, string? Namespace, string? Key) Decision(
        MetadataReader reader,
        Dictionary<string, TypeDefinitionHandle> types,
        string serialized)
    {
        // An argument naming a type in another assembly is assembly-qualified; the decision types are
        // generated into the citing assembly, so the name is all there is to look up.
        int comma = serialized.IndexOf(',');
        string name = (comma < 0 ? serialized : serialized.Substring(0, comma)).Trim();

        if (!types.TryGetValue(name, out TypeDefinitionHandle handle))
        {
            return (null, null, null);
        }

        string? id = null;
        string? ns = null;
        string? key = null;

        foreach (FieldDefinitionHandle fieldHandle in reader.GetTypeDefinition(handle).GetFields())
        {
            FieldDefinition field = reader.GetFieldDefinition(fieldHandle);
            ConstantHandle constant = field.GetDefaultValue();
            if (constant.IsNil)
            {
                continue;
            }

            Constant value = reader.GetConstant(constant);
            if (value.TypeCode != ConstantTypeCode.String)
            {
                continue;
            }

            BlobReader blob = reader.GetBlobReader(value.Value);
            string text = blob.ReadUTF16(blob.Length);

            switch (reader.GetString(field.Name))
            {
                case "Id":
                    id = text;
                    break;
                case "Namespace":
                    ns = text;
                    break;
                case "Key":
                    key = text;
                    break;
            }
        }

        return (id, ns, key);
    }
}
