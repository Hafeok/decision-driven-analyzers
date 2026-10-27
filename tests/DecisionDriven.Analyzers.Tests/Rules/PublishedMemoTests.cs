using System.Collections.Immutable;
using DecisionDriven.Analyzers.Rules;
using Microsoft.CodeAnalysis;
using Xunit;

namespace DecisionDriven.Analyzers.Tests.Rules;

/// <summary>
/// DD0019 and the published-once memo: a <c>HashSet&lt;T&gt;</c> or <c>Dictionary&lt;K, V&gt;</c>
/// built in a local, published to a <c>[Memoized]</c> field once, and only read afterwards.
/// </summary>
/// <remarks>
/// <c>ImmutableModel.MemoizedFieldsAreVerified</c> (#119). Two first readers may each build a
/// collection, but each fills only its own local and one of them wins the publication, so no reader
/// sees a collection being filled. Every way out of the form has its own finding, pinned verbatim.
/// </remarks>
public sealed class PublishedMemoTests
{
    private const string Memo = "[global::DecisionDriven.Memoized] ";

    private const string Set = "System.Collections.Generic.HashSet<long>";

    private const string Map = "System.Collections.Generic.Dictionary<int, string>";

    private const string Exchange = "System.Threading.Interlocked.CompareExchange";

    private const string Source = "private readonly System.Collections.Immutable.ImmutableArray<long> source = System.Collections.Immutable.ImmutableArray.Create(1L, 2L); ";

    private const string Ids = Source + Memo + "private " + Set + "? ids; ";

    private const string Read = "public bool Allows(long id) => ids is not null && ids.Contains(id); ";

    private const string ComputesDesignChange =
        "compute it only from readonly fields, other [Memoized] fields, parameters and calls that change nothing, or compute it in the constructor";

    private const string EscapeDesignChange =
        "fill the local only through its own members, such as Add and the indexer, and use it nowhere else until it is published";

    private const string WriteAfterDesignChange =
        "finish filling the local before publishing it, and after that use only the collection's reads";

    private const string StoreDesignChange =
        "build a new HashSet<T> or Dictionary<K, V> in a local and publish it once, by 'Interlocked.CompareExchange(ref ids, local, null)' or 'ids ??= local'";

    private const string HandOutDesignChange =
        "use only its reads, such as Contains, TryGetValue, Count and the indexer get, and return what they return, not the collection";

    [Theory]
    // Built inline from a readonly field, published by CompareExchange, read through Contains.
    [InlineData(Ids + "public bool Allows(long id) { if (ids is null) { " + Set + " built = new(); foreach (long x in source) { built.Add(x); } " + Exchange + "(ref ids, built, null); } return ids!.Contains(id); }")]
    // Published by ??=.
    [InlineData(Ids + Read + "public void Load() { " + Set + " built = new(); foreach (long x in source) { built.Add(x * 2); } ids ??= built; }")]
    // A Dictionary filled through its indexer, and its reads: TryGetValue, Count, the indexer get, foreach.
    [InlineData(Memo + "private " + Map + "? names; public void Load(System.Collections.Generic.IEnumerable<int> from) { " + Map + " built = new(); foreach (int i in from) { built[i] = i.ToString(); } names ??= built; } "
        + "public string? Name(int id) => names is not null && names.TryGetValue(id, out string? name) ? name : null; public int Count() => names?.Count ?? 0; public string At(int id) => names![id]; public int Sum() { int sum = 0; if (names is null) { return 0; } foreach (var pair in names) { sum += pair.Key; } return sum; }")]
    // A field typed as the read-only interface, the local made from a parameter.
    [InlineData(Memo + "private System.Collections.Generic.IReadOnlySet<long>? ids; public bool Allows(long id, System.Collections.Generic.IEnumerable<long> from) { if (ids is null) { " + Set + " built = new(from); " + Exchange + "(ref ids, built, null); } return ids.Contains(id); }")]
    // A read-only dictionary interface, the local filled by a collection initializer.
    [InlineData(Memo + "private System.Collections.Generic.IReadOnlyDictionary<int, string>? names; public string? Name(int id) { if (names is null) { " + Map + " built = new() { [1] = \"one\", [2] = \"two\" }; names ??= built; } return names.TryGetValue(id, out string? name) ? name : null; }")]
    // Built by a private static helper, followed one level.
    [InlineData(Ids + Read + "public void Load() { " + Set + " built = Build(source); " + Exchange + "(ref ids, built, null); } private static " + Set + " Build(System.Collections.Immutable.ImmutableArray<long> from) { " + Set + " made = new(); foreach (long x in from) { made.Add(x); } return made; }")]
    // Built by a private instance helper that returns the collection it creates.
    [InlineData(Ids + Read + "public void Load() { " + Set + " built = Build(); built.Add(0); ids ??= built; } private " + Set + " Build() => new " + Set + "(source);")]
    // Read through ?. and written through ?. nowhere.
    [InlineData(Ids + "public bool Allows(long id) => ids?.Contains(id) ?? false; public int Count() => ids?.Count ?? 0; public void Load() { " + Set + " built = new(source); ids ??= built; }")]
    // The local read after its publication.
    [InlineData(Ids + Read + "public int Load() { " + Set + " built = new(source); ids ??= built; return built.Count; }")]
    public void A_collection_built_in_a_local_and_published_once_is_not_reported(string members)
    {
        Assert.Empty(Run(members));
    }

    public static TheoryData<string, string, string> Violations() => new TheoryData<string, string, string>
    {
        // The local passed out before publication.
        {
            Ids + Read + "public void Load() { " + Set + " built = new(); Fill(built); ids ??= built; } private static void Fill(" + Set + " into) { }",
            "'Consumer.Model.Value.Load' lets 'built', the collection it publishes to [Memoized] 'ids', out before publishing it, so whoever has it can write to it after it is published",
            EscapeDesignChange
        },
        // Captured by a lambda, which may run after publication.
        {
            Ids + Read + "public int Load() { " + Set + " built = new(); System.Func<int> count = () => built.Count; ids ??= built; return count(); }",
            "'Consumer.Model.Value.Load' lets 'built', the collection it publishes to [Memoized] 'ids', out before publishing it, so whoever has it can write to it after it is published",
            EscapeDesignChange
        },
        // A helper's local escaping: reported on the helper.
        {
            Ids + Read + "public void Load() { " + Set + " built = Build(); ids ??= built; } private static " + Set + " Build() { " + Set + " made = new(); Keep(made); return made; } private static void Keep(object kept) { }",
            "'Consumer.Model.Value.Build' lets 'made', the collection it publishes to [Memoized] 'ids', out before publishing it, so whoever has it can write to it after it is published",
            EscapeDesignChange
        },
        // Written through the local after publication.
        {
            Ids + Read + "public void Load() { " + Set + " built = new(source); ids ??= built; built.Add(3); }",
            "'Consumer.Model.Value.Load' writes to [Memoized] 'ids' through 'built.Add' after it is published, so a reader can see it change",
            WriteAfterDesignChange
        },
        // Written through the field.
        {
            Ids + Read + "public void Load() { " + Set + " built = new(source); ids ??= built; } public void More(long id) { if (ids is not null) { ids.Add(id); } }",
            "'Consumer.Model.Value.More' writes to [Memoized] 'ids' through 'ids.Add' after it is published, so a reader can see it change",
            WriteAfterDesignChange
        },
        // Written through ?.
        {
            Ids + Read + "public void Load() { " + Set + " built = new(source); ids ??= built; } public void More(long id) => ids?.Add(id);",
            "'Consumer.Model.Value.More' writes to [Memoized] 'ids' through 'ids.Add' after it is published, so a reader can see it change",
            WriteAfterDesignChange
        },
        // Written through the field's indexer.
        {
            Memo + "private " + Map + "? names; public void Load() { " + Map + " built = new(); names ??= built; } public void Rename(int id) { if (names is not null) { names[id] = \"x\"; } }",
            "'Consumer.Model.Value.Rename' writes to [Memoized] 'names' through 'names.this[]' after it is published, so a reader can see it change",
            "finish filling the local before publishing it, and after that use only the collection's reads"
        },
        // A plain store.
        {
            Ids + Read + "public void Load() { " + Set + " built = new(source); ids = built; }",
            "'Consumer.Model.Value.Load' stores into [Memoized] 'ids' something other than a collection it has just built in a local, so readers can share a collection that is still changing",
            StoreDesignChange
        },
        // A new collection, but not through a local.
        {
            Ids + Read + "public void Load() => ids ??= new " + Set + "(source);",
            "'Consumer.Model.Value.Load' stores into [Memoized] 'ids' something other than a collection it has just built in a local, so readers can share a collection that is still changing",
            StoreDesignChange
        },
        // A local that does not hold a collection the member created.
        {
            Ids + Read + "public void Load() { " + Set + " built = System.Linq.Enumerable.ToHashSet(source); ids ??= built; }",
            "'Consumer.Model.Value.Load' stores into [Memoized] 'ids' something other than a collection it has just built in a local, so readers can share a collection that is still changing",
            StoreDesignChange
        },
        // CompareExchange against something other than null.
        {
            Ids + Read + "public void Load() { " + Set + " built = new(source); " + Exchange + "(ref ids, built, built); }",
            "'Consumer.Model.Value.Load' stores into [Memoized] 'ids' something other than a collection it has just built in a local, so readers can share a collection that is still changing",
            StoreDesignChange
        },
        // A helper that returns something it did not create.
        {
            Ids + Read + "private readonly " + Set + " fixedIds = new(); public void Load() { " + Set + " built = Build(); ids ??= built; } private " + Set + " Build() => fixedIds;",
            "'Consumer.Model.Value.Load' stores into [Memoized] 'ids' something other than a collection it has just built in a local, so readers can share a collection that is still changing",
            StoreDesignChange
        },
        // Handed out: returned.
        {
            Ids + "public System.Collections.Generic.IReadOnlySet<long>? All() => ids; public void Load() { " + Set + " built = new(source); ids ??= built; }",
            "'Consumer.Model.Value.All' hands out the [Memoized] 'ids' itself, and whoever gets it can write to it",
            HandOutDesignChange
        },
        // Handed out: passed.
        {
            Ids + "public int Count() => ids is null ? 0 : System.Linq.Enumerable.Count(ids); public void Load() { " + Set + " built = new(source); ids ??= built; }",
            "'Consumer.Model.Value.Count' hands out the [Memoized] 'ids' itself, and whoever gets it can write to it",
            HandOutDesignChange
        },
        // Handed out: what CompareExchange returns is stored.
        {
            Ids + Read + "public void Load() { " + Set + " built = new(source); object? previous = " + Exchange + "(ref ids, built, null); }",
            "'Consumer.Model.Value.Load' hands out the [Memoized] 'ids' itself, and whoever gets it can write to it",
            HandOutDesignChange
        },
        // Built from mutable state.
        {
            Memo + "private " + Set + "? ids; private long seed; " + Read + "public void Load() { " + Set + " built = new(); built.Add(seed); ids ??= built; }",
            "'Consumer.Model.Value.Load' computes [Memoized] 'ids' from 'seed', which is neither readonly nor [Memoized], so what is memoised depends on when it was first asked for",
            ComputesDesignChange
        },
        // A helper built from mutable state: reported on the helper.
        {
            Memo + "private " + Set + "? ids; private long seed; " + Read + "public void Load() { " + Set + " built = Build(); ids ??= built; } private " + Set + " Build() => new " + Set + " { seed };",
            "'Consumer.Model.Value.Build' computes [Memoized] 'ids' from 'seed', which is neither readonly nor [Memoized], so what is memoised depends on when it was first asked for",
            ComputesDesignChange
        },
        // Mutable elements.
        {
            Memo + "private System.Collections.Generic.HashSet<System.Text.StringBuilder>? texts; public int Count() => texts?.Count ?? 0;",
            "'Consumer.Model.Value.texts' is [Memoized] and the elements it holds, 'System.Text.StringBuilder', are mutable, so what it holds can change under whoever reads it",
            "hold immutable elements: a primitive, string, enum or immutable type"
        },
        // Mutable values.
        {
            Memo + "private System.Collections.Generic.Dictionary<int, System.Text.StringBuilder>? texts; public int Count() => texts?.Count ?? 0;",
            "'Consumer.Model.Value.texts' is [Memoized] and the values it holds, 'System.Text.StringBuilder', are mutable, so what it holds can change under whoever reads it",
            "hold immutable values: a primitive, string, enum or immutable type"
        },
        // Two members publishing.
        {
            Ids + Read + "public void Load() { " + Set + " built = new(source); ids ??= built; } public void Reload() { " + Set + " built = new(); ids ??= built; }",
            "'Consumer.Model.Value.ids' is [Memoized] and is written by both 'Load' and 'Reload', so which computation it holds depends on which ran first",
            "write it in one member and have the others read it through that one"
        },
    };

    [Theory]
    [MemberData(nameof(Violations))]
    public void A_departure_from_the_published_once_form_is_reported_with_its_own_message(string members, string finding, string designChange)
    {
        Diagnostic diagnostic = Assert.Single(Run(members));

        Assert.Equal("DD0019", diagnostic.Id);
        Assert.Equal(DiagnosticSeverity.Error, diagnostic.Severity);
        Assert.Equal(
            finding + ". Decide: " + designChange
                + " | mark it [DesignDecision(typeof(<Set>.<Key>), Scope = ExceptionScope.<Scope>)] citing the accepted decision that says so. "
                + "Do not add the attribute without a decision that answers this; if the reason is only that the code already looked like this, take the design change.",
            diagnostic.GetMessage());
    }

    [Fact]
    public void A_readonly_set_is_still_a_plain_collection()
    {
        // A readonly field cannot be published once; it is the filled-in-place collection #119 leaves refused.
        Diagnostic diagnostic = Assert.Single(Run(Memo + "private readonly " + Set + " ids = new(); public bool See(long id) => ids.Add(id);"));

        Assert.StartsWith("'Consumer.Model.Value.ids' is [Memoized] and is a plain 'System.Collections.Generic.HashSet<long>'", diagnostic.GetMessage(), System.StringComparison.Ordinal);
    }

    [Fact]
    public void Without_the_attribute_the_same_publication_is_reported()
    {
        // The control: the store is a write to a field, and only [Memoized] makes it verified instead.
        Diagnostic diagnostic = Assert.Single(Run(Source + "private " + Set + "? ids; " + Read + "public void Load() { " + Set + " built = new(source); ids ??= built; }"));

        Assert.StartsWith("'Consumer.Model.Value.Load' writes 'ids'", diagnostic.GetMessage(), System.StringComparison.Ordinal);
    }

    private static ImmutableArray<Diagnostic> Run(string members) =>
        RuleHarness.Run(
            new MutableModelAnalyzer(),
            ContractSource.File(
                "namespace Consumer.Model { public sealed class Value { " + members + " } }",
                "[assembly: global::DecisionDriven.DomainModel(\"Consumer.Model\", " + ContractSource.Decision + ")]"),
            assemblyName: "Consumer",
            archLayer: 1);
}
