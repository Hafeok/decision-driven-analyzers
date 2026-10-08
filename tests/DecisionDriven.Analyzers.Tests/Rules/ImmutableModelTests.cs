using System;
using System.Collections.Immutable;
using DecisionDriven.Analyzers.Rules;
using Microsoft.CodeAnalysis;
using Xunit;

namespace DecisionDriven.Analyzers.Tests.Rules;

/// <summary>
/// DD0019: a value handed out by a read cannot be changed by whoever got it.
/// </summary>
public sealed class ImmutableModelTests
{
    [Theory]
    [InlineData("public sealed class Quad { public int Count { get; set; } }", "has a public setter")]
    [InlineData("public sealed class Quad { public int Count { get; internal set; } }", "has an internal setter")]
    [InlineData("public sealed class Quad { public int Count; }", "is a field that is not readonly")]
    [InlineData("public sealed class Quad { public System.Collections.Generic.List<int> Items { get; } = new(); }", "which the caller can add to and remove from")]
    [InlineData("public sealed class Quad { public int[] Items { get; } = new int[0]; }", "which the caller can add to and remove from")]
    [InlineData("public sealed class Quad { public System.Collections.Generic.IList<int> Items { get; } = null!; }", "which the caller can add to and remove from")]
    [InlineData("public struct Quad { public int Count { get; } }", "is a model struct and is not readonly")]
    public void A_mutable_model_type_is_reported(string body, string expected)
    {
        Diagnostic diagnostic = Assert.Single(Run(body));

        Assert.Equal("DD0019", diagnostic.Id);
        Assert.Equal(DiagnosticSeverity.Error, diagnostic.Severity);
        Assert.Contains(expected, diagnostic.GetMessage(), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("public sealed class Quad { public int Count { get; init; } }")]
    [InlineData("public sealed class Quad { public readonly int Count; }")]
    [InlineData("public sealed class Quad { public System.Collections.Immutable.ImmutableArray<int> Items { get; init; } }")]
    [InlineData("public sealed class Quad { public System.Collections.Generic.IReadOnlyList<int> Items { get; init; } = null!; }")]
    [InlineData("public sealed class Quad { public System.ReadOnlyMemory<int> Items { get; init; } }")]
    [InlineData("public sealed class Quad { public System.Collections.Frozen.FrozenDictionary<int, int> Items { get; init; } = null!; }")]
    [InlineData("public readonly record struct Quad(int Count);")]
    public void An_immutable_model_type_is_not_reported(string body)
    {
        Assert.Empty(Run(body));
    }

    [Fact]
    public void A_sealed_builder_in_the_same_namespace_is_not_reported()
    {
        // ImmutableModel.BuildersAreTheEscapeHatch. Mutation while a value is being assembled is
        // fine; what is not fine is the value staying mutable after it is handed over.
        Assert.Empty(Run(
            "public sealed class QuadBuilder { public int Count { get; set; } "
            + "public System.Collections.Generic.List<int> Items { get; } = new(); }"));
    }

    [Fact]
    public void A_builder_that_is_not_sealed_is_reported()
    {
        Diagnostic diagnostic = Assert.Single(Run(
            "public class QuadBuilder { public int Count { get; set; } }"));

        Assert.Contains("is a builder and is not sealed", diagnostic.GetMessage(), StringComparison.Ordinal);
    }

    [Fact]
    public void A_builder_on_a_contract_signature_is_reported_by_DD0010()
    {
        // ADR-A11 says DD0010 already prevents this "since a builder is neither model vocabulary
        // nor a contract". It did not: a builder is in a [DomainModel] namespace by that same ADR,
        // so the namespace alone let it through. DD0010 now excludes builders from that allowance.
        Diagnostic diagnostic = Assert.Single(RuleHarness.Run(
            new ContractVocabularyAnalyzer(),
            File(
                "namespace Consumer.Model { public sealed class QuadBuilder { public int Count { get; set; } } }"
                + Environment.NewLine
                + "namespace Consumer { " + ContractSource.Contract
                + " public interface IQuadSource { global::Consumer.Model.QuadBuilder Read(); } }"),
            assemblyName: "Consumer",
            archLayer: 1));

        Assert.Equal("DD0010", diagnostic.Id);
        Assert.Contains(
            "is a builder, which is mutable by design and belongs to the code assembling a value",
            diagnostic.GetMessage(),
            StringComparison.Ordinal);
    }

    [Fact]
    public void A_type_outside_the_model_is_not_checked()
    {
        Assert.Empty(RuleHarness.Run(
            new MutableModelAnalyzer(),
            File("namespace Consumer.Scratch { public sealed class Quad { public int Count { get; set; } } }"),
            assemblyName: "Consumer",
            archLayer: 1));
    }

    [Fact]
    public void An_internal_model_type_is_not_checked()
    {
        // Not handed out, so nobody outside holds one to change.
        Assert.Empty(Run("internal sealed class Quad { public int Count { get; set; } }"));
    }

    [Fact]
    public void A_public_type_nested_in_an_internal_one_is_not_checked()
    {
        // Declared public, reachable by nobody outside: effective visibility, not the keyword.
        Assert.Empty(Run("internal sealed class Store { public sealed class Quad { public int Count { get; set; } } }"));
    }

    private const string Set = "private readonly System.Collections.Generic.HashSet<int> items = new(); ";

    [Theory]
    [InlineData(Set + "public bool Add(int item) => items.Add(item);", "'Consumer.Model.Dataset.Add' calls 'items.Add' on the 'System.Collections.Generic.HashSet<int>' it holds")]
    [InlineData("private int count; public void Increment() => count++;", "'Consumer.Model.Dataset.Increment' writes 'count'")]
    [InlineData("private int count; public void Reset() { this.count = 0; }", "'Consumer.Model.Dataset.Reset' writes 'count'")]
    [InlineData("private int count; internal void Bump() => System.Threading.Interlocked.Increment(ref count);", "'Consumer.Model.Dataset.Bump' writes 'count'")]
    [InlineData("private readonly System.Collections.Generic.Dictionary<string, int> counts = new(); public void Set(string key, int value) => counts[key] = value;", "calls 'counts.this[]' on the 'System.Collections.Generic.Dictionary<string, int>' it holds")]
    [InlineData(Set + "public void Add(int item) => Insert(item); private void Insert(int item) => items.Add(item);", "'Consumer.Model.Dataset.Add' calls 'Insert', which calls 'items.Add' on the 'System.Collections.Generic.HashSet<int>' it holds")]
    public void A_method_that_changes_the_value_it_belongs_to_is_reported(string members, string expected)
    {
        // ImmutableModel.DomainModelImmutable, amended to name method bodies: every member is private
        // and readonly, and the type is as mutable as a type can be.
        Diagnostic diagnostic = Assert.Single(Run("public sealed class Dataset { " + members + " }"));

        Assert.Equal("DD0019", diagnostic.Id);
        Assert.Contains(expected, diagnostic.GetMessage(), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("private readonly System.Collections.Immutable.ImmutableHashSet<int> items = System.Collections.Immutable.ImmutableHashSet<int>.Empty; public bool Has(int item) => items.Contains(item);")]
    [InlineData("public int Count { get; init; } public Dataset WithCount(int count) => new Dataset { Count = count };")]
    [InlineData("private readonly int count; public Dataset(int count) { this.count = count; } public int Read() => count;")]
    [InlineData(Set + "private void Insert(int item) => items.Add(item);")]
    [InlineData(Set + "public void Add(int item) => First(item); private void First(int item) => Second(item); private void Second(int item) => items.Add(item);")]
    [InlineData("private readonly object gate = new(); public string Describe() => gate.ToString()!;")]
    [InlineData("private readonly System.Func<int> next = () => 1; public int Next() => next();")]
    public void A_method_that_does_not_change_the_value_or_changes_it_out_of_reach_is_not_reported(string members)
    {
        // Out of reach: a private method nobody outside can call, and mutation two helpers deep,
        // which the decision says is out of scope. Calling a delegate, or object's own members on a
        // field, changes nothing.
        Assert.Empty(Run("public sealed class Dataset { " + members + " }"));
    }

    [Fact]
    public void A_method_marked_with_DesignDecision_is_not_reported()
    {
        Assert.Empty(Run(
            "public sealed class Dataset { " + Set
            + "[global::DecisionDriven.DesignDecision(" + ContractSource.Decision + ", Scope = global::DecisionDriven.ExceptionScope.Pool)] "
            + "public bool Add(int item) => items.Add(item); }"));
    }

    [Fact]
    public void A_builders_methods_are_not_checked()
    {
        Assert.Empty(Run("public sealed class DatasetBuilder { " + Set + "public DatasetBuilder Add(int item) { items.Add(item); return this; } }"));
    }

    [Fact]
    public void The_message_for_a_method_that_changes_its_value_is_exactly_this()
    {
        Assert.Equal(
            "'Consumer.Model.Dataset.Add' calls 'items.Add' on the 'System.Collections.Generic.HashSet<int>' it holds, so a value already handed out changes under whoever holds it. "
            + "Decide: return a new value with the change applied instead of changing this one, or do the changing in a sealed *Builder and build the value from it "
            + "| mark it [DesignDecision(typeof(<Set>.<Key>), Scope = ExceptionScope.<Scope>)] "
            + "citing the accepted decision that says so. "
            + "Do not add the attribute without a decision that answers this; if the reason is only that "
            + "the code already looked like this, take the design change.",
            Assert.Single(Run("public sealed class Dataset { " + Set + "public bool Add(int item) => items.Add(item); }")).GetMessage());
    }

    [Fact]
    public void The_message_is_exactly_this()
    {
        // DiagnosticMessages.ExactMessageTested.
        Assert.Equal(
            "'Consumer.Model.Quad.Count' has a public setter. "
            + "Decide: make it 'init', or take the setter off and set the value in the constructor "
            + "| mark it [DesignDecision(typeof(<Set>.<Key>), Scope = ExceptionScope.<Scope>)] "
            + "citing the accepted decision that says so. "
            + "Do not add the attribute without a decision that answers this; if the reason is only that "
            + "the code already looked like this, take the design change.",
            Assert.Single(Run("public sealed class Quad { public int Count { get; set; } }")).GetMessage());
    }

    private static ImmutableArray<Diagnostic> Run(string body) =>
        RuleHarness.Run(
            new MutableModelAnalyzer(),
            File("namespace Consumer.Model { " + body + " }"),
            assemblyName: "Consumer",
            archLayer: 1);

    private static string File(string source) =>
        ContractSource.File(
            source,
            "[assembly: global::DecisionDriven.DomainModel(\"Consumer.Model\", " + ContractSource.Decision + ")]");
}
