using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection.Metadata;
using DecisionDriven.Report.Metadata;

namespace DecisionDriven.Report.Metrics;

/// <summary>One model type's LCOM4.</summary>
internal sealed record CohesionRow(string Type, int Components, int Methods);

/// <summary>
/// LCOM4 for every type in a <c>[DomainModel]</c> namespace: the number of connected groups its
/// methods fall into, where two methods are connected when they touch a common field or one calls
/// the other.
/// </summary>
/// <remarks>
/// <para>
/// One is a type with one responsibility. Two or more is a type whose methods split into groups
/// that share nothing - two types in one declaration. It is a report, not a gate, because ADR-A06
/// found cohesion metrics too noisy to fail a build on and penalising span-based code by
/// construction; the gate on cohesion is on names.
/// </para>
/// <para>
/// Accessors are not nodes, and a method that reaches a property through its getter counts as
/// touching the property's field. Counted as nodes, every property would be its own component and
/// a plain <c>record Quad(int A, int B)</c> would score two - one "responsibility" per property,
/// on exactly the types a <c>[DomainModel]</c> namespace is full of. The other members a compiler
/// writes - a record's equality, printing and deconstruction - are left out too: they touch every
/// field by construction and would glue any type into one component. A type left with no methods
/// is data, and has no row.
/// </para>
/// </remarks>
internal static class Cohesion
{
    internal static IReadOnlyList<CohesionRow> Compute(IReadOnlyList<LoadedAssembly> assemblies)
    {
        List<CohesionRow> rows = new List<CohesionRow>();

        foreach (LoadedAssembly assembly in assemblies)
        {
            MetadataReader reader = assembly.Reader;
            List<string> model = Types.DomainModelPrefixes(reader);
            if (model.Count == 0)
            {
                continue;
            }

            foreach (TypeDefinitionHandle handle in reader.TypeDefinitions)
            {
                TypeDefinition type = reader.GetTypeDefinition(handle);

                if (!Types.IsWritten(reader, handle)
                    || Types.IsInterface(type)
                    || !Types.InNamespaces(reader.GetString(type.Namespace), model))
                {
                    continue;
                }

                if (Measure(assembly, handle, type) is { } row)
                {
                    rows.Add(row);
                }
            }
        }

        return rows.OrderBy(row => row.Type, StringComparer.Ordinal).ToList();
    }

    private static CohesionRow? Measure(LoadedAssembly assembly, TypeDefinitionHandle handle, TypeDefinition type)
    {
        MetadataReader reader = assembly.Reader;

        HashSet<MethodDefinitionHandle> accessors = new HashSet<MethodDefinitionHandle>();
        foreach (PropertyDefinitionHandle property in type.GetProperties())
        {
            PropertyAccessors pair = reader.GetPropertyDefinition(property).GetAccessors();
            accessors.Add(pair.Getter);
            accessors.Add(pair.Setter);
        }

        foreach (EventDefinitionHandle @event in type.GetEvents())
        {
            EventAccessors pair = reader.GetEventDefinition(@event).GetAccessors();
            accessors.Add(pair.Adder);
            accessors.Add(pair.Remover);
        }

        // What each accessor touches, so a method calling one is credited with the field behind it.
        Dictionary<MethodDefinitionHandle, List<FieldDefinitionHandle>> accessorFields =
            new Dictionary<MethodDefinitionHandle, List<FieldDefinitionHandle>>();

        foreach (MethodDefinitionHandle accessor in accessors)
        {
            if (!accessor.IsNil)
            {
                accessorFields[accessor] = OwnFields(assembly, handle, accessor).ToList();
            }
        }

        List<MethodDefinitionHandle> methods = new List<MethodDefinitionHandle>();
        foreach (MethodDefinitionHandle methodHandle in type.GetMethods())
        {
            MethodDefinition method = reader.GetMethodDefinition(methodHandle);
            string name = reader.GetString(method.Name);

            if (name is ".ctor" or ".cctor"
                || accessors.Contains(methodHandle)
                || Types.IsCompilerGenerated(reader, method.GetCustomAttributes()))
            {
                continue;
            }

            methods.Add(methodHandle);
        }

        if (methods.Count == 0)
        {
            return null;
        }

        Dictionary<MethodDefinitionHandle, int> index = new Dictionary<MethodDefinitionHandle, int>();
        for (int i = 0; i < methods.Count; i++)
        {
            index[methods[i]] = i;
        }

        int[] parent = Enumerable.Range(0, methods.Count).ToArray();
        Dictionary<FieldDefinitionHandle, int> firstToucher = new Dictionary<FieldDefinitionHandle, int>();

        void Touch(int method, FieldDefinitionHandle field)
        {
            if (firstToucher.TryGetValue(field, out int other))
            {
                Union(parent, method, other);
            }
            else
            {
                firstToucher.Add(field, method);
            }
        }

        for (int i = 0; i < methods.Count; i++)
        {
            if (assembly.Body(reader.GetMethodDefinition(methods[i])) is not { } body)
            {
                continue;
            }

            foreach ((Il.Use use, EntityHandle operand) in Il.Members(body))
            {
                if (use == Il.Use.Field
                    && operand.Kind == HandleKind.FieldDefinition
                    && reader.GetFieldDefinition((FieldDefinitionHandle)operand).GetDeclaringType() == handle)
                {
                    Touch(i, (FieldDefinitionHandle)operand);
                }
                else if (use == Il.Use.Call && operand.Kind == HandleKind.MethodDefinition)
                {
                    MethodDefinitionHandle callee = (MethodDefinitionHandle)operand;

                    if (index.TryGetValue(callee, out int other))
                    {
                        Union(parent, i, other);
                    }
                    else if (accessorFields.TryGetValue(callee, out List<FieldDefinitionHandle>? fields))
                    {
                        foreach (FieldDefinitionHandle field in fields)
                        {
                            Touch(i, field);
                        }
                    }
                }
            }
        }

        int components = Enumerable.Range(0, methods.Count).Select(i => Find(parent, i)).Distinct().Count();

        return new CohesionRow(TypeNames.Of(reader, handle, '.'), components, methods.Count);
    }

    /// <summary>The fields of <paramref name="type"/> that <paramref name="method"/> reads or writes.</summary>
    private static IEnumerable<FieldDefinitionHandle> OwnFields(LoadedAssembly assembly, TypeDefinitionHandle type, MethodDefinitionHandle method)
    {
        MetadataReader reader = assembly.Reader;

        if (assembly.Body(reader.GetMethodDefinition(method)) is not { } body)
        {
            yield break;
        }

        foreach ((Il.Use use, EntityHandle operand) in Il.Members(body))
        {
            if (use == Il.Use.Field
                && operand.Kind == HandleKind.FieldDefinition
                && reader.GetFieldDefinition((FieldDefinitionHandle)operand).GetDeclaringType() == type)
            {
                yield return (FieldDefinitionHandle)operand;
            }
        }
    }

    private static int Find(int[] parent, int i)
    {
        while (parent[i] != i)
        {
            parent[i] = parent[parent[i]];
            i = parent[i];
        }

        return i;
    }

    private static void Union(int[] parent, int a, int b) => parent[Find(parent, a)] = Find(parent, b);
}
