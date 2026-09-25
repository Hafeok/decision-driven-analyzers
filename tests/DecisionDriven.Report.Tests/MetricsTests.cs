using System;
using System.Collections.Generic;
using System.Linq;
using DecisionDriven.Report.Metadata;
using DecisionDriven.Report.Metrics;
using DecisionDriven.Report.Tests.Fixtures;
using Xunit;

namespace DecisionDriven.Report.Tests;

/// <summary>
/// The three whole-graph metrics, over assemblies compiled for real.
/// </summary>
public sealed class MetricsTests
{
    private const string Store =
        "[assembly: DecisionDriven.ArchLayer(0)]\n"
        + "namespace Fix.Store {\n"
        + "  [DecisionDriven.Contract(typeof(DecisionDriven.Ledger.Fixture.Shape.StoreContract), Role = \"store\")]\n"
        + "  public interface IStore { int Read(); void Write(int value); int Count { get; } }\n"
        + "  public sealed class Store : IStore { public int Read() => 1; public void Write(int value) { } public int Count => 0; }\n"
        + "}\n";

    private const string Service =
        "[assembly: DecisionDriven.ArchLayer(1)]\n"
        + "namespace Fix.Service {\n"
        + "  public sealed class Reader { public int Go(Fix.Store.IStore store) => store.Read(); }\n"
        + "  public sealed class Watcher { public System.Func<int> Watch(Fix.Store.IStore store) => () => store.Count; }\n"
        + "}\n";

    [Fact]
    public void Coupling_instability_abstractness_and_distance_are_computed_between_layered_assemblies()
    {
        using Scratch scratch = new Scratch();
        using Assemblies built = Two(scratch);

        IReadOnlyList<PackageRow> rows = PackageMetrics.Compute(built.All);

        PackageRow store = Assert.Single(rows, row => row.Assembly == "Fix.Store");
        PackageRow service = Assert.Single(rows, row => row.Assembly == "Fix.Service");

        // Fix.Service references Fix.Store and nothing references Fix.Service.
        Assert.Equal((1, 0, 0.0), (store.Afferent, store.Efferent, store.Instability));
        Assert.Equal((0, 1, 1.0), (service.Afferent, service.Efferent, service.Instability));

        // One interface and one class that somebody wrote; the generated markers and the decision
        // type do not count, or every assembly's abstractness would be a measure of the generator.
        Assert.Equal(0.5, store.Abstractness);
        Assert.Equal(0.5, store.Distance);
        Assert.Equal(0.0, service.Abstractness);
        Assert.Equal(0.0, service.Distance);

        Assert.False(store.ContradictsLayer);
        Assert.False(service.ContradictsLayer);
    }

    [Fact]
    public void An_assembly_less_stable_than_one_above_it_contradicts_its_layer()
    {
        using Scratch scratch = new Scratch();

        // Same shape, layers swapped: the thing everything depends on now claims the higher layer.
        using Assemblies built = Two(scratch, storeLayer: 1, serviceLayer: 0);

        PackageRow service = Assert.Single(PackageMetrics.Compute(built.All), row => row.Assembly == "Fix.Service");

        Assert.True(service.ContradictsLayer);
    }

    [Fact]
    public void An_assembly_with_no_declared_layer_is_not_on_the_main_sequence()
    {
        using Scratch scratch = new Scratch();
        string unlayered = Build.Assembly(scratch.PathOf("bin"), "Fix.Unlayered", new Dictionary<string, string>
        {
            [scratch.PathOf("u/U.cs")] = "namespace Fix.Unlayered { public sealed class U { } }",
            [scratch.PathOf("u/Markers.cs")] = Build.Markers,
        });

        using Assemblies built = new Assemblies(unlayered);

        Assert.Empty(PackageMetrics.Compute(built.All));
    }

    [Fact]
    public void A_contract_reports_its_members_implementers_and_what_each_caller_uses()
    {
        using Scratch scratch = new Scratch();
        using Assemblies built = Two(scratch);

        ContractRow row = Assert.Single(ContractUsage.Compute(built.All));

        Assert.Equal("Fix.Store.IStore", row.Contract);
        Assert.Equal(new[] { "Count", "Read", "Write" }, row.Members);
        Assert.Equal(1, row.Implementers);

        // Across the assembly boundary, which is the only use that matters. The lambda's call is
        // credited to the type that wrote the lambda, not to its compiler-generated closure.
        Assert.Collection(
            row.Callers,
            reader =>
            {
                Assert.Equal("Fix.Service.Reader", reader.Caller);
                Assert.Equal(new[] { "Read" }, reader.Members);
            },
            watcher =>
            {
                Assert.Equal("Fix.Service.Watcher", watcher.Caller);
                Assert.Equal(new[] { "Count" }, watcher.Members);
            });
    }

    [Fact]
    public void LCOM4_counts_groups_of_methods_that_share_nothing()
    {
        using Scratch scratch = new Scratch();
        string model = Build.Assembly(scratch.PathOf("bin"), "Fix.Model", new Dictionary<string, string>
        {
            [scratch.PathOf("m/Model.cs")] =
                "[assembly: DecisionDriven.DomainModel(\"Fix.Model\", typeof(DecisionDriven.Ledger.Fixture.Shape.ModelNamespace))]\n"
                + "namespace Fix.Model {\n"
                // Two methods, two fields, nothing shared: two types in one declaration.
                + "  public sealed class Split { private int a; private int b; public int A() => a; public int B() => b; }\n"
                // Two methods over one field.
                + "  public sealed class Whole { private int a; public int A() => a; public int Twice() => a * 2; }\n"
                // Joined through a property: a method reaching the field through its getter touches it.
                + "  public sealed class Joined { public int Value { get; init; } public int A() => Value; public int B() => Value + 1; }\n"
                // Data and nothing else: no methods, so no row rather than a meaningless number.
                + "  public sealed record Quad(int A, int B);\n"
                + "}\n",
            [scratch.PathOf("m/Markers.cs")] = Build.Markers,
            [scratch.PathOf("m/Decisions.cs")] = Build.Decision("fixture", "Shape", "ModelNamespace"),
        });

        using Assemblies built = new Assemblies(model);

        Dictionary<string, int> lcom4 = Cohesion.Compute(built.All).ToDictionary(row => row.Type, row => row.Components);

        Assert.Equal(2, lcom4["Fix.Model.Split"]);
        Assert.Equal(1, lcom4["Fix.Model.Whole"]);
        Assert.Equal(1, lcom4["Fix.Model.Joined"]);
        Assert.False(lcom4.ContainsKey("Fix.Model.Quad"));
    }

    private static Assemblies Two(Scratch scratch, int storeLayer = 0, int serviceLayer = 1)
    {
        string store = Build.Assembly(scratch.PathOf("bin"), "Fix.Store", new Dictionary<string, string>
        {
            [scratch.PathOf("store/Store.cs")] = Store.Replace("ArchLayer(0)", "ArchLayer(" + storeLayer + ")", StringComparison.Ordinal),
            [scratch.PathOf("store/Markers.cs")] = Build.Markers,
            [scratch.PathOf("store/Decisions.cs")] = Build.Decision("fixture", "Shape", "StoreContract"),
        });

        string service = Build.Assembly(
            scratch.PathOf("bin"),
            "Fix.Service",
            new Dictionary<string, string>
            {
                [scratch.PathOf("service/Service.cs")] = Service.Replace("ArchLayer(1)", "ArchLayer(" + serviceLayer + ")", StringComparison.Ordinal),
                [scratch.PathOf("service/Markers.cs")] = Build.Markers,
            },
            new[] { store });

        return new Assemblies(store, service);
    }

    /// <summary>The loaded assemblies, disposed together.</summary>
    private sealed class Assemblies : IDisposable
    {
        internal Assemblies(params string[] paths)
        {
            All = paths.Select(path => LoadedAssembly.Open(path)!).ToList();
        }

        internal List<LoadedAssembly> All { get; }

        public void Dispose()
        {
            foreach (LoadedAssembly assembly in All)
            {
                assembly.Dispose();
            }
        }
    }
}
