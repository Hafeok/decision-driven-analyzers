using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection.Metadata;
using DecisionDriven.Report.Metadata;

namespace DecisionDriven.Report.Metrics;

/// <summary>One layered assembly's place on the main sequence.</summary>
internal sealed record PackageRow(
    string Assembly,
    int Layer,
    int Afferent,
    int Efferent,
    double Instability,
    double Abstractness,
    double Distance,
    bool ContradictsLayer);

/// <summary>
/// Afferent and efferent coupling, instability, abstractness and distance from the main sequence,
/// for every assembly that declares a layer.
/// </summary>
/// <remarks>
/// <para>
/// <c>WholeGraphReport.WholeGraphMetricsAreCiTool</c>. The layering rule guarantees that references
/// point downward; it cannot say the layers are the right ones. Instability beside the declared
/// layer is the check on that: a low layer everything depends on should be stable, and one whose
/// instability is higher than a layer above it has its dependencies the wrong way round in spirit
/// even where DD0001 is satisfied to the letter.
/// </para>
/// <para>
/// Coupling is counted between the layered assemblies given, by the assembly references each one's
/// metadata actually carries. The compiler only writes a reference for an assembly whose types are
/// used, so an unused project reference does not count - which is the coupling that exists rather
/// than the coupling somebody declared.
/// </para>
/// </remarks>
internal static class PackageMetrics
{
    internal static IReadOnlyList<PackageRow> Compute(IReadOnlyList<LoadedAssembly> assemblies)
    {
        Dictionary<string, (LoadedAssembly Assembly, int Layer)> layered =
            new Dictionary<string, (LoadedAssembly, int)>(StringComparer.Ordinal);

        foreach (LoadedAssembly assembly in assemblies)
        {
            if (Types.ArchLayer(assembly.Reader) is int layer)
            {
                layered[assembly.Name] = (assembly, layer);
            }
        }

        Dictionary<string, HashSet<string>> efferent = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);
        Dictionary<string, HashSet<string>> afferent = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);

        foreach (string name in layered.Keys)
        {
            efferent[name] = new HashSet<string>(StringComparer.Ordinal);
            afferent[name] = new HashSet<string>(StringComparer.Ordinal);
        }

        foreach ((string name, (LoadedAssembly assembly, int _)) in layered)
        {
            foreach (string referenced in assembly.References())
            {
                if (referenced != name && layered.ContainsKey(referenced))
                {
                    efferent[name].Add(referenced);
                    afferent[referenced].Add(name);
                }
            }
        }

        List<PackageRow> rows = new List<PackageRow>();

        foreach ((string name, (LoadedAssembly assembly, int layer)) in layered)
        {
            int ca = afferent[name].Count;
            int ce = efferent[name].Count;
            double instability = ca + ce == 0 ? 0 : (double)ce / (ca + ce);
            double abstractness = Abstractness(assembly.Reader);

            rows.Add(new PackageRow(
                name,
                layer,
                ca,
                ce,
                instability,
                abstractness,
                Math.Abs(abstractness + instability - 1),
                ContradictsLayer: false));
        }

        // An assembly contradicts its layer when something at a higher layer is more stable than it.
        // Stability should increase downward; where it does not, the report says which.
        return rows
            .Select(row => row with
            {
                ContradictsLayer = rows.Any(other => other.Layer > row.Layer && other.Instability < row.Instability),
            })
            .OrderBy(row => row.Layer)
            .ThenBy(row => row.Assembly, StringComparer.Ordinal)
            .ToList();
    }

    /// <summary>Abstract types over all types somebody wrote.</summary>
    private static double Abstractness(MetadataReader reader)
    {
        int total = 0;
        int @abstract = 0;

        foreach (TypeDefinitionHandle handle in reader.TypeDefinitions)
        {
            if (!Types.IsWritten(reader, handle))
            {
                continue;
            }

            total++;
            if (Types.IsAbstract(reader.GetTypeDefinition(handle)))
            {
                @abstract++;
            }
        }

        return total == 0 ? 0 : (double)@abstract / total;
    }
}
