using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using DecisionDriven.Analyzers.Rules;
using DecisionDriven.Analyzers.Tests.Ledger;
using Microsoft.CodeAnalysis;
using Xunit;

namespace DecisionDriven.Analyzers.Tests.Rules;

/// <summary>
/// DD0019 and <c>[Memoized]</c>: a memo of a pure computation is verified, not reported.
/// </summary>
/// <remarks>
/// <c>ImmutableModel.MemoizedFieldsAreVerified</c>. Two forms are accepted, a value filled once and a
/// <c>ConcurrentDictionary</c> filled through <c>GetOrAdd</c> with a static factory; every way out
/// of them has its own finding, and each finding is pinned here verbatim.
/// </remarks>
public sealed class MemoizedFieldTests
{
    private const string Memo = "[global::DecisionDriven.Memoized] ";

    private const string Cache = "System.Collections.Concurrent.ConcurrentDictionary<int, string>";

    private const string Names = "private readonly string first = \"a\"; private readonly string last = \"b\"; ";

    [Theory]
    // A cached int hash, guarded by a sentinel.
    [InlineData(Names + Memo + "private int hash; public override int GetHashCode() { if (hash == 0) hash = System.HashCode.Combine(first, last); return hash; } public override bool Equals(object? other) => ReferenceEquals(this, other);")]
    // The same with ??= on a nullable.
    [InlineData(Names + Memo + "private int? hash; public override int GetHashCode() => hash ??= System.HashCode.Combine(first, last); public override bool Equals(object? other) => ReferenceEquals(this, other);")]
    // The same with a local copy, assigned in the block.
    [InlineData(Names + Memo + "private int hash; public int Hash() { int h = hash; if (h == 0) { h = System.HashCode.Combine(first, last); hash = h; } return h; }")]
    // A local copy assigned in one statement.
    [InlineData(Names + Memo + "private int hash; public int Hash() { var h = hash; if (h is 0) hash = h = first.Length + last.Length; return h; }")]
    // A cached string, from an interpolation of readonly fields.
    [InlineData(Names + Memo + "private string? text; public override string ToString() => text ??= $\"{first} {last}\";")]
    // A cached string in a property getter, tested against null.
    [InlineData(Names + Memo + "private string? text; public string Text { get { if (text is null) { text = string.Concat(first, \" \", last); } return text; } }")]
    // Computed from a private helper that changes nothing, and from another memo.
    [InlineData(Names + Memo + "private string? text; " + Memo + "private int? size; public int Size() => size ??= Describe().Length; public string Describe() => text ??= Join(); private string Join() => first + last;")]
    // An immutable value: an ImmutableArray built lazily, from a parameter.
    [InlineData(Memo + "private System.Collections.Immutable.ImmutableArray<int>? sorted; public System.Collections.Immutable.ImmutableArray<int> Sorted(System.Collections.Generic.IEnumerable<int> from) => sorted ??= System.Collections.Immutable.ImmutableArray.CreateRange(System.Linq.Enumerable.OrderBy(from, static x => x));")]
    // An enum.
    [InlineData("public enum Kind { None, Short, Long } " + Names + Memo + "private Kind kind; public Kind Classify() { if (kind == Kind.None) kind = first.Length > 3 ? Kind.Long : Kind.Short; return kind; }")]
    // A per-key memo with a static lambda.
    [InlineData(Memo + "private readonly " + Cache + " names = new(); public string Name(int id) => names.GetOrAdd(id, static i => i.ToString());")]
    // A static lambda with an argument.
    [InlineData(Names + Memo + "private readonly " + Cache + " names = new(); public string Name(int id) => names.GetOrAdd(id, static (i, prefix) => prefix + i, first);")]
    // A static method group, and the dictionary's reads.
    [InlineData(Memo + "private readonly " + Cache + " names = new(); public string Name(int id) => names.GetOrAdd(id, Render); private static string Render(int i) => i.ToString(); public bool Known(int id) => names.ContainsKey(id); public int Count() => names.Count; public bool TryName(int id, out string? name) => names.TryGetValue(id, out name);")]
    // #118: a null test of a per-key memo is a read, in each of its four spellings.
    [InlineData(Memo + "private readonly " + Cache + "? names; public string Name(int id) => names is not null ? names.GetOrAdd(id, static i => i.ToString()) : id.ToString();")]
    [InlineData(Memo + "private readonly " + Cache + "? names; public string Name(int id) { if (names is null) { return id.ToString(); } return names.GetOrAdd(id, static i => i.ToString()); }")]
    [InlineData(Memo + "private readonly " + Cache + "? names; public bool Memoising() => names != null;")]
    [InlineData(Memo + "private readonly " + Cache + "? names; public string Name(int id) => null == names ? id.ToString() : names.GetOrAdd(id, static i => i.ToString());")]
    public void A_memo_in_one_of_the_two_forms_is_not_reported(string members)
    {
        Assert.Empty(Run(members));
    }

    [Fact]
    public void Without_the_attribute_the_same_cache_is_reported()
    {
        // The control: what [Memoized] changes is that the write is verified, not that it is allowed.
        Diagnostic diagnostic = Assert.Single(Run(Names + "private int hash; public override int GetHashCode() { if (hash == 0) hash = System.HashCode.Combine(first, last); return hash; } public override bool Equals(object? other) => ReferenceEquals(this, other);"));

        Assert.Contains("'Consumer.Model.Value.GetHashCode' writes 'hash'", diagnostic.GetMessage(), StringComparison.Ordinal);
    }

    public static TheoryData<string, string, string> Violations() => new TheoryData<string, string, string>
    {
        // A plain Dictionary memo: a race.
        {
            Memo + "private readonly System.Collections.Generic.Dictionary<int, string> names = new(); public string Name(int id) { if (!names.TryGetValue(id, out string? name)) { name = id.ToString(); names[id] = name; } return name; }",
            "'Consumer.Model.Value.names' is [Memoized] and is a plain 'System.Collections.Generic.Dictionary<int, string>', and a plain collection filled by concurrent readers is a race",
            "hold a readonly System.Collections.Concurrent.ConcurrentDictionary<K, V> filled only through GetOrAdd with a static factory, compute it in the constructor, or build it in a sealed *Builder"
        },
        // A [Memoized] List<T> returned to callers.
        {
            Memo + "private System.Collections.Generic.List<int>? items; public System.Collections.Generic.List<int> Items() => items ??= new System.Collections.Generic.List<int> { 1, 2 };",
            "'Consumer.Model.Value.items' is [Memoized] and is a plain 'System.Collections.Generic.List<int>', and a plain collection filled by concurrent readers is a race",
            "hold a readonly System.Collections.Concurrent.ConcurrentDictionary<K, V> filled only through GetOrAdd with a static factory, compute it in the constructor, or build it in a sealed *Builder"
        },
        // A plain HashSet.
        {
            Memo + "private readonly System.Collections.Generic.HashSet<int> seen = new(); public bool See(int id) => seen.Add(id);",
            "'Consumer.Model.Value.seen' is [Memoized] and is a plain 'System.Collections.Generic.HashSet<int>', and a plain collection filled by concurrent readers is a race",
            "hold a readonly System.Collections.Concurrent.ConcurrentDictionary<K, V> filled only through GetOrAdd with a static factory, compute it in the constructor, or build it in a sealed *Builder"
        },
        // A mutable type.
        {
            Memo + "private System.Text.StringBuilder? text; public string Text() => (text ??= new System.Text.StringBuilder(\"a\")).ToString();",
            "'Consumer.Model.Value.text' is [Memoized] and its type, 'System.Text.StringBuilder', is mutable, so the value it caches can change under whoever reads it",
            "memoise a primitive, string, enum or immutable type, compute it in the constructor, or build it in a sealed *Builder"
        },
        // A per-key memo of mutable values.
        {
            Memo + "private readonly System.Collections.Concurrent.ConcurrentDictionary<int, System.Text.StringBuilder> names = new(); public string Name(int id) => names.GetOrAdd(id, static i => new System.Text.StringBuilder()).ToString();",
            "'Consumer.Model.Value.names' is [Memoized] and the values it holds, 'System.Text.StringBuilder', are mutable, so a value memoised for a key can change under whoever got it",
            "memoise an immutable value per key: a primitive, string, enum or immutable type"
        },
        // A per-key memo that is not readonly.
        {
            Memo + "private " + Cache + " names = new(); public string Name(int id) => names.GetOrAdd(id, static i => i.ToString());",
            "'Consumer.Model.Value.names' is a [Memoized] ConcurrentDictionary that is not readonly, so the dictionary itself can be replaced",
            "make it readonly"
        },
        // Not private.
        {
            Memo + "internal int hash; public int Hash() => hash;",
            "'Consumer.Model.Value.hash' is [Memoized] and is not private, so code outside the type can write to it",
            "make it private"
        },
        // Static.
        {
            Memo + "private static int hash; public int Hash() => hash;",
            "'Consumer.Model.Value.hash' is [Memoized] and static, so it is shared by every value rather than belonging to one",
            "memoise per instance, or compute it once in a static readonly initializer"
        },
        // Readonly scalar: nothing to memoise.
        {
            Memo + "private readonly int hash = 3; public int Hash() => hash;",
            "'Consumer.Model.Value.hash' is [Memoized] and readonly, so it is set in the constructor and there is nothing to memoise",
            "take [Memoized] off"
        },
        // Two writing members.
        {
            Names + Memo + "private int? hash; public int Hash() => hash ??= first.Length; public int Other() => hash ??= last.Length;",
            "'Consumer.Model.Value.hash' is [Memoized] and is written by both 'Hash' and 'Other', so which computation it holds depends on which ran first",
            "write it in one member and have the others read it through that one"
        },
        // Two members filling a per-key memo.
        {
            Memo + "private readonly " + Cache + " names = new(); public string Name(int id) => names.GetOrAdd(id, static i => i.ToString()); public string Label(int id) => names.GetOrAdd(id, static i => \"#\" + i);",
            "'Consumer.Model.Value.names' is [Memoized] and is written by both 'Name' and 'Label', so which computation it holds depends on which ran first",
            "write it in one member and have the others read it through that one"
        },
        // A capturing factory.
        {
            Names + Memo + "private readonly " + Cache + " names = new(); public string Name(int id) => names.GetOrAdd(id, i => first + i);",
            "'Consumer.Model.Value.Name' passes 'names.GetOrAdd' a factory that is not a static lambda or a static method, so it can capture state and what is memoised for a key depends on who asked first",
            "make the factory a static lambda or a static method, and pass what it needs as GetOrAdd's third argument"
        },
        // An instance method group is a capture of this.
        {
            Memo + "private readonly " + Cache + " names = new(); public string Name(int id) => names.GetOrAdd(id, Render); private string Render(int i) => i.ToString();",
            "'Consumer.Model.Value.Name' passes 'names.GetOrAdd' a factory that is not a static lambda or a static method, so it can capture state and what is memoised for a key depends on who asked first",
            "make the factory a static lambda or a static method, and pass what it needs as GetOrAdd's third argument"
        },
        // Any other write to a value memo: unguarded.
        {
            Names + Memo + "private int hash; public int Hash() { hash = first.Length; return hash; }",
            "'Consumer.Model.Value.Hash' writes [Memoized] 'hash' other than by 'hash ??= ...' or 'if (hash == sentinel) hash = ...', so the value can change after it was first read",
            "write it only in one of those two forms, or take [Memoized] off and compute it in the constructor"
        },
        // Any other write: a step.
        {
            Memo + "private int hash; public int Hash() => ++hash;",
            "'Consumer.Model.Value.Hash' writes [Memoized] 'hash' other than by 'hash ??= ...' or 'if (hash == sentinel) hash = ...', so the value can change after it was first read",
            "write it only in one of those two forms, or take [Memoized] off and compute it in the constructor"
        },
        // Any other write: by ref.
        {
            Memo + "private string? text; public string Text() => System.Threading.Interlocked.CompareExchange(ref text, \"a\", null) ?? \"a\";",
            "'Consumer.Model.Value.Text' writes [Memoized] 'text' other than by 'text ??= ...' or 'if (text == sentinel) text = ...', so the value can change after it was first read",
            "write it only in one of those two forms, or take [Memoized] off and compute it in the constructor"
        },
        // Any other write: the guard tests something else.
        {
            Names + Memo + "private int hash; public int Hash() { if (first.Length == 1) hash = last.Length; return hash; }",
            "'Consumer.Model.Value.Hash' writes [Memoized] 'hash' other than by 'hash ??= ...' or 'if (hash == sentinel) hash = ...', so the value can change after it was first read",
            "write it only in one of those two forms, or take [Memoized] off and compute it in the constructor"
        },
        // Any other write to a per-key memo.
        {
            Memo + "private readonly " + Cache + " names = new(); public void Name(int id, string name) => names[id] = name;",
            "'Consumer.Model.Value.Name' calls 'names.this[]' on the [Memoized] 'System.Collections.Concurrent.ConcurrentDictionary<int, string>', which is neither GetOrAdd with a static factory nor a read, so a value memoised for a key can be replaced",
            "fill it only through GetOrAdd(key, static factory) or GetOrAdd(key, static factory, arg)"
        },
        {
            Memo + "private readonly " + Cache + " names = new(); public bool Name(int id, string name) => names.TryAdd(id, name);",
            "'Consumer.Model.Value.Name' calls 'names.TryAdd' on the [Memoized] 'System.Collections.Concurrent.ConcurrentDictionary<int, string>', which is neither GetOrAdd with a static factory nor a read, so a value memoised for a key can be replaced",
            "fill it only through GetOrAdd(key, static factory) or GetOrAdd(key, static factory, arg)"
        },
        {
            Memo + "private readonly " + Cache + " names = new(); public string Name(int id, string name) => names.GetOrAdd(id, name);",
            "'Consumer.Model.Value.Name' calls 'names.GetOrAdd(key, value)' on the [Memoized] 'System.Collections.Concurrent.ConcurrentDictionary<int, string>', which is neither GetOrAdd with a static factory nor a read, so a value memoised for a key can be replaced",
            "fill it only through GetOrAdd(key, static factory) or GetOrAdd(key, static factory, arg)"
        },
        // Handing the dictionary itself out.
        {
            Memo + "private readonly " + Cache + " names = new(); public System.Collections.Generic.IDictionary<int, string> All() => names;",
            "'Consumer.Model.Value.All' hands out the [Memoized] 'names' itself, and whoever gets it can write to it",
            "return what GetOrAdd or a read returns, not the dictionary"
        },
        // #118: a null test beside it does not excuse a real hand-out. Returned.
        {
            Memo + "private readonly " + Cache + "? names; public object? All() => names is not null ? names : null;",
            "'Consumer.Model.Value.All' hands out the [Memoized] 'names' itself, and whoever gets it can write to it",
            "return what GetOrAdd or a read returns, not the dictionary"
        },
        // Stored.
        {
            Memo + "private readonly " + Cache + "? names; public void Keep(object?[] into) { if (names != null) into[0] = names; }",
            "'Consumer.Model.Value.Keep' hands out the [Memoized] 'names' itself, and whoever gets it can write to it",
            "return what GetOrAdd or a read returns, not the dictionary"
        },
        // Passed.
        {
            Memo + "private readonly " + Cache + "? names; public int Count() => names == null ? 0 : System.Linq.Enumerable.Count(names);",
            "'Consumer.Model.Value.Count' hands out the [Memoized] 'names' itself, and whoever gets it can write to it",
            "return what GetOrAdd or a read returns, not the dictionary"
        },
        // A computation that reads mutable state.
        {
            Memo + "private int? hash; private int seed; public int Hash() => hash ??= seed * 31;",
            "'Consumer.Model.Value.Hash' computes [Memoized] 'hash' from 'seed', which is neither readonly nor [Memoized], so what is memoised depends on when it was first asked for",
            "compute it only from readonly fields, other [Memoized] fields, parameters and calls that change nothing, or compute it in the constructor"
        },
        // A computation that calls a writing member of something mutable.
        {
            Memo + "private int? count; public int Count(System.Collections.Generic.List<int> from) => count ??= from.Remove(1) ? 1 : 0;",
            "'Consumer.Model.Value.Count' computes [Memoized] 'count' from 'Remove' on 'System.Collections.Generic.List<int>', which the rule cannot show changes nothing, so what is memoised depends on when it was first asked for",
            "compute it only from readonly fields, other [Memoized] fields, parameters and calls that change nothing, or compute it in the constructor"
        },
        // A computation that reads a property of the type with a setter.
        {
            Memo + "private int? hash; private int Seed { get; set; } public int Hash() => hash ??= Seed * 31;",
            "'Consumer.Model.Value.Hash' computes [Memoized] 'hash' from 'Seed', which has a setter, so what is memoised depends on when it was first asked for",
            "compute it only from readonly fields, other [Memoized] fields, parameters and calls that change nothing, or compute it in the constructor"
        },
        // A local copy given something mutable.
        {
            Memo + "private int hash; private int seed; public int Hash() { int h = hash; if (h == 0) { h = seed; hash = h; } return h; }",
            "'Consumer.Model.Value.Hash' computes [Memoized] 'hash' from 'seed', which is neither readonly nor [Memoized], so what is memoised depends on when it was first asked for",
            "compute it only from readonly fields, other [Memoized] fields, parameters and calls that change nothing, or compute it in the constructor"
        },
        // A computation through a delegate.
        {
            "private readonly System.Func<int> next = () => 1; " + Memo + "private int? hash; public int Hash() => hash ??= next();",
            "'Consumer.Model.Value.Hash' computes [Memoized] 'hash' from a call through a delegate, which the rule cannot show changes nothing, so what is memoised depends on when it was first asked for",
            "compute it only from readonly fields, other [Memoized] fields, parameters and calls that change nothing, or compute it in the constructor"
        },
        // The factory's argument is this: a capture by another name.
        {
            Memo + "private readonly " + Cache + " names = new(); public string Name(int id) => names.GetOrAdd(id, static (i, self) => self.ToString() + i, this);",
            "'Consumer.Model.Value.Name' computes [Memoized] 'names' from 'this' itself, which the computation could change, so what is memoised depends on when it was first asked for",
            "compute it only from readonly fields, other [Memoized] fields, parameters and calls that change nothing, or compute it in the constructor"
        },
    };

    [Theory]
    [MemberData(nameof(Violations))]
    public void A_memo_outside_the_two_forms_is_reported_with_its_own_message(string members, string finding, string designChange)
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
    public void A_memo_tested_against_null_is_not_handed_out()
    {
        // #118's reproduction: a per-key memo that may be absent, tested with 'is not null'.
        // The usings go inside the namespace: the harness puts the assembly attribute first in the file.
        string source = @"
namespace Sample.Model
{
    using System.Collections.Concurrent;
    using DecisionDriven;

    public readonly record struct Key(int Value);

    public enum Memo
    {
        Off,
        On,
    }

    public sealed class Lookup
    {
        [Memoized]
        private readonly ConcurrentDictionary<Key, bool>? _cache;

        public Lookup(Memo memo) => _cache = memo == Memo.On ? new ConcurrentDictionary<Key, bool>() : null;

        public bool Allows(Key key) => _cache is not null ? _cache.GetOrAdd(key, static k => Decide(k)) : Decide(key);

        private static bool Decide(Key key) => key.Value % 2 == 0;
    }

    public sealed class AlwaysMemoised
    {
        [Memoized]
        private readonly ConcurrentDictionary<Key, bool> _cache = new();

        public bool Allows(Key key) => _cache.GetOrAdd(key, static k => k.Value % 2 == 0);
    }
}";

        Assert.Empty(RuleHarness.Run(
            new MutableModelAnalyzer(),
            ContractSource.File(source, "[assembly: global::DecisionDriven.DomainModel(\"Sample.Model\", " + ContractSource.Decision + ")]"),
            assemblyName: "Sample.Model",
            archLayer: 1));
    }

    [Fact]
    public void A_memo_written_in_a_private_method_and_nowhere_else_is_one_writer()
    {
        // The writer may be private: the memo is filled from wherever it is filled, and one member
        // filling it is what matters. Reading it from a second member is not writing it.
        Assert.Empty(Run(Names + Memo + "private int? hash; public int Hash() => Compute(); public int Again() => hash ?? Compute(); private int Compute() => hash ??= first.Length;"));
    }

    [Fact]
    public void A_memo_computed_by_a_method_that_changes_the_value_is_reported_and_so_is_the_method()
    {
        // Two findings, both true: the memo's computation is not pure, and Count changes the value
        // through Fill whether or not anything is memoised.
        string[] messages = Run(
            "private readonly System.Collections.Generic.List<int> items = new(); " + Memo
            + "private int? count; public int Count() => count ??= Fill(); private int Fill() { items.Add(1); return items.Count; }")
            .Select(d => d.GetMessage())
            .OrderBy(m => m, StringComparer.Ordinal)
            .ToArray();

        Assert.Equal(2, messages.Length);
        Assert.StartsWith("'Consumer.Model.Value.Count' calls 'Fill', which calls 'items.Add'", messages[0], StringComparison.Ordinal);
        Assert.StartsWith("'Consumer.Model.Value.Count' computes [Memoized] 'count' from 'Fill', which the rule cannot show changes nothing", messages[1], StringComparison.Ordinal);
    }

    [Fact]
    public void A_memo_with_a_wrong_shape_is_reported_once_not_again_for_its_writes()
    {
        Diagnostic diagnostic = Assert.Single(Run(Memo + "private readonly System.Collections.Generic.Dictionary<int, string> names = new(); public void A(int id) => names[id] = \"a\"; public void B(int id) => names.Remove(id);"));

        Assert.Contains("is a plain 'System.Collections.Generic.Dictionary<int, string>'", diagnostic.GetMessage(), StringComparison.Ordinal);
    }

    [Fact]
    public void A_memo_field_marked_with_DesignDecision_is_not_checked()
    {
        Assert.Empty(Run(
            "[global::DecisionDriven.DesignDecision(" + ContractSource.Decision + ", Scope = global::DecisionDriven.ExceptionScope.Pool)] "
            + Memo + "private readonly System.Collections.Generic.Dictionary<int, string> names = new(); public void A(int id) => names[id] = \"a\";"));
    }

    [Fact]
    public void A_memo_on_a_public_field_is_reported_once()
    {
        // Not again as "a field that is not readonly": the memo check owns a [Memoized] field.
        Diagnostic diagnostic = Assert.Single(Run(Memo + "public int hash;"));

        Assert.Contains("is [Memoized] and is not private", diagnostic.GetMessage(), StringComparison.Ordinal);
    }

    [Fact]
    public void The_generated_attribute_is_the_one_DD0019_reads()
    {
        // The marker as the generator emits it, not the harness's copy: if the two drifted apart,
        // every memo in every consumer would be reported and the tests above would still pass.
        GeneratorHarness.Result result = GeneratorHarness.Run(
            "[assembly: global::DecisionDriven.DomainModel(\"Consumer.Model\", typeof(global::DecisionDriven.Ledger.SampleNs.SampleSet.SampleKey))]"
                + Environment.NewLine
                + "namespace Consumer.Model { public sealed class Value { "
                + Names + Memo + "private int? hash; public int Hash() => hash ??= first.Length; "
                + Memo + "private readonly System.Collections.Generic.List<int> items = new(); public int Count() => items.Count; } }",
            new List<GeneratorHarness.LedgerFile>
            {
                LedgerInput.AsSet(LedgerInput.FrontMatter("sample-set", "SampleKey", "A statement.")),
            },
            assemblyName: "Consumer");

        ImmutableArray<Diagnostic> diagnostics = RuleHarness.RunOn(
            new MutableModelAnalyzer(),
            result.Compilation,
            new Dictionary<string, string>(StringComparer.Ordinal) { ["build_property.ArchLayer"] = "1" });

        Diagnostic diagnostic = Assert.Single(diagnostics);
        Assert.Contains("'Consumer.Model.Value.items' is [Memoized] and is a plain", diagnostic.GetMessage(), StringComparison.Ordinal);
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
