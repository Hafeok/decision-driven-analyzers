using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection.Metadata;
using DecisionDriven.Report.Metadata;

namespace DecisionDriven.Report.Metrics;

/// <summary>Which members of a contract one calling type uses.</summary>
internal sealed record CallerRow(string Caller, IReadOnlyList<string> Members);

/// <summary>A contract's size, its implementers, and how much of it each caller uses.</summary>
internal sealed record ContractRow(
    string Contract,
    IReadOnlyList<string> Members,
    int Implementers,
    IReadOnlyList<CallerRow> Callers);

/// <summary>
/// Member count, implementer count, and the members each caller uses, per <c>[Contract]</c>
/// interface.
/// </summary>
/// <remarks>
/// <c>Contracts.MemberCountIsReport</c>. ADR-A08 rejected member count as a gate because it measures
/// the wrong thing: <c>IEnumerator&lt;T&gt;</c> has three members and one segregation failure, and a
/// four-member streaming sink has none. What does measure it is the gap between what an interface
/// offers and what each caller takes. A contract whose every caller uses two of its nine members is
/// two contracts that have not been told yet.
/// </remarks>
internal static class ContractUsage
{
    internal static IReadOnlyList<ContractRow> Compute(IReadOnlyList<LoadedAssembly> assemblies)
    {
        // Every [Contract] interface, with its members by name.
        Dictionary<Members.TypeKey, SortedSet<string>> contracts = new Dictionary<Members.TypeKey, SortedSet<string>>();

        foreach (LoadedAssembly assembly in assemblies)
        {
            MetadataReader reader = assembly.Reader;

            foreach (TypeDefinitionHandle handle in reader.TypeDefinitions)
            {
                TypeDefinition type = reader.GetTypeDefinition(handle);

                if (!Types.IsInterface(type) || !Types.Has(reader, type.GetCustomAttributes(), Attributes.Contract))
                {
                    continue;
                }

                SortedSet<string> members = new SortedSet<string>(StringComparer.Ordinal);
                foreach (MethodDefinitionHandle method in type.GetMethods())
                {
                    members.Add(Members.MemberOf(reader.GetString(reader.GetMethodDefinition(method).Name)));
                }

                contracts[new Members.TypeKey(assembly.Name, TypeNames.Of(reader, handle))] = members;
            }
        }

        Dictionary<Members.TypeKey, int> implementers = contracts.Keys.ToDictionary(key => key, _ => 0);
        Dictionary<Members.TypeKey, Dictionary<string, SortedSet<string>>> callers =
            contracts.Keys.ToDictionary(key => key, _ => new Dictionary<string, SortedSet<string>>(StringComparer.Ordinal));

        foreach (LoadedAssembly assembly in assemblies)
        {
            MetadataReader reader = assembly.Reader;

            foreach (TypeDefinitionHandle handle in reader.TypeDefinitions)
            {
                TypeDefinition type = reader.GetTypeDefinition(handle);

                foreach (InterfaceImplementationHandle implementation in type.GetInterfaceImplementations())
                {
                    if (Members.OwnerOf(assembly, reader.GetInterfaceImplementation(implementation).Interface) is { } key
                        && implementers.ContainsKey(key))
                    {
                        implementers[key]++;
                    }
                }

                string author = TypeNames.Of(reader, Types.Author(reader, handle), '.');

                foreach (MethodDefinitionHandle methodHandle in type.GetMethods())
                {
                    if (assembly.Body(reader.GetMethodDefinition(methodHandle)) is not { } body)
                    {
                        continue;
                    }

                    foreach ((Il.Use use, EntityHandle operand) in Il.Members(body))
                    {
                        if (use != Il.Use.Call
                            || Members.Called(assembly, operand) is not ({ } owner, { } name)
                            || !callers.TryGetValue(owner, out Dictionary<string, SortedSet<string>>? byCaller))
                        {
                            continue;
                        }

                        if (!byCaller.TryGetValue(author, out SortedSet<string>? used))
                        {
                            used = new SortedSet<string>(StringComparer.Ordinal);
                            byCaller.Add(author, used);
                        }

                        used.Add(Members.MemberOf(name));
                    }
                }
            }
        }

        return contracts
            .OrderBy(pair => pair.Key.FullName, StringComparer.Ordinal)
            .Select(pair => new ContractRow(
                pair.Key.FullName.Replace('+', '.'),
                pair.Value.ToList(),
                implementers[pair.Key],
                callers[pair.Key]
                    .OrderBy(caller => caller.Key, StringComparer.Ordinal)
                    .Select(caller => new CallerRow(caller.Key, caller.Value.ToList()))
                    .ToList()))
            .ToList();
    }
}
