using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Metadata;
using DecisionDriven.Report.Metadata;

namespace DecisionDriven.Report.Metrics;

/// <summary>One namespace of one assembly: how many types it holds, and how many of them are public.</summary>
internal sealed record NamespaceRow(string Namespace, int PublicTypes, int TotalTypes);

/// <summary>One assembly's namespaces, largest first.</summary>
internal sealed record AssemblyShapeRow(string Assembly, IReadOnlyList<NamespaceRow> Namespaces)
{
    /// <summary>The namespace with the most types, or null for an assembly with none.</summary>
    internal NamespaceRow? Largest => Namespaces.Count == 0 ? null : Namespaces[0];
}

/// <summary>
/// Per assembly, its namespaces with public and total type counts.
/// </summary>
/// <remarks>
/// <para>
/// <c>WholeGraphReport.NamespaceShapeInReport</c>. DD0022 and DD0023 check that whatever namespace
/// structure exists is real: files sit where their namespaces say, and the namespaces form a DAG.
/// Neither says how large a namespace has grown, and no rule does
/// (<c>NamesAndNamespaces.NamespaceDepthNotRequired</c>): a single-namespace assembly conforms.
/// This is the number that lets a reader see a namespace hiding several concepts, and it is a
/// report rather than a gate (<c>NamesAndNamespaces.CohesionMetricsAreReport</c>,
/// <c>WholeGraphReport.NothingGatesUntilBaselineDecision</c>). A consumer that wants a cap writes
/// its own decision naming the threshold and the baseline.
/// </para>
/// <para>
/// A type is counted when somebody wrote it (<see cref="Types.IsWritten"/>), a nested type in the
/// namespace of its outermost containing type, which is the same node DD0023 attributes it to.
/// Public means visible outside the assembly: a public top-level type, or a nested type public at
/// every level. Namespaces are sorted by total type count descending, then by name, so the first
/// row is the largest.
/// </para>
/// </remarks>
internal static class NamespaceShape
{
    internal static IReadOnlyList<AssemblyShapeRow> Compute(IReadOnlyList<LoadedAssembly> assemblies)
    {
        List<AssemblyShapeRow> rows = new List<AssemblyShapeRow>();

        foreach (LoadedAssembly assembly in assemblies)
        {
            MetadataReader reader = assembly.Reader;
            Dictionary<string, (int Public, int Total)> counts = new Dictionary<string, (int, int)>(StringComparer.Ordinal);

            foreach (TypeDefinitionHandle handle in reader.TypeDefinitions)
            {
                if (!Types.IsWritten(reader, handle))
                {
                    continue;
                }

                string ns = NamespaceOf(reader, handle);
                counts.TryGetValue(ns, out (int Public, int Total) count);
                counts[ns] = (count.Public + (IsPublic(reader, handle) ? 1 : 0), count.Total + 1);
            }

            rows.Add(new AssemblyShapeRow(
                assembly.Name,
                counts
                    .Select(entry => new NamespaceRow(entry.Key, entry.Value.Public, entry.Value.Total))
                    .OrderByDescending(row => row.TotalTypes)
                    .ThenBy(row => row.Namespace, StringComparer.Ordinal)
                    .ToList()));
        }

        return rows.OrderBy(row => row.Assembly, StringComparer.Ordinal).ToList();
    }

    /// <summary>The namespace of the outermost containing type; the global namespace is the empty string.</summary>
    private static string NamespaceOf(MetadataReader reader, TypeDefinitionHandle handle)
    {
        TypeDefinition type = reader.GetTypeDefinition(handle);
        TypeDefinitionHandle declaring = type.GetDeclaringType();

        return declaring.IsNil ? reader.GetString(type.Namespace) : NamespaceOf(reader, declaring);
    }

    /// <summary>Public at every level of nesting.</summary>
    private static bool IsPublic(MetadataReader reader, TypeDefinitionHandle handle)
    {
        TypeDefinition type = reader.GetTypeDefinition(handle);
        TypeAttributes visibility = type.Attributes & TypeAttributes.VisibilityMask;
        TypeDefinitionHandle declaring = type.GetDeclaringType();

        return declaring.IsNil
            ? visibility == TypeAttributes.Public
            : visibility == TypeAttributes.NestedPublic && IsPublic(reader, declaring);
    }
}
