using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using DecisionDriven.Analyzers.Rules;
using Microsoft.CodeAnalysis;
using Xunit;

namespace DecisionDriven.Analyzers.Tests.Rules;

/// <summary>
/// DD0013: a model or contract surface says what a value is, not what it is made of.
/// </summary>
public sealed class PrimitiveSurfaceTests
{
    [Theory]
    [InlineData("string Read();", "string")]
    [InlineData("long Read();", "long")]
    [InlineData("System.Guid Read();", "System.Guid")]
    [InlineData("System.DateTimeOffset Read();", "System.DateTimeOffset")]
    [InlineData("object Read();", "object")]
    [InlineData("string[] Read();", "string")]
    [InlineData("System.Collections.Generic.IEnumerable<int> Read();", "int")]
    [InlineData("System.Threading.Tasks.Task<string> Read();", "string")]
    [InlineData("System.Threading.Tasks.ValueTask<long> Read();", "long")]
    public void A_naked_primitive_on_a_contract_is_reported(string member, string primitive)
    {
        Diagnostic diagnostic = Assert.Single(Contract(member));

        Assert.Equal("DD0013", diagnostic.Id);
        Assert.Equal(DiagnosticSeverity.Error, diagnostic.Severity);
        Assert.Contains("'" + primitive + "'", diagnostic.GetMessage(), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("bool Read();")]
    [InlineData("void Read(System.ReadOnlySpan<byte> bytes);")]
    [InlineData("void Read(System.Memory<char> chars);")]
    [InlineData("void Read(System.Threading.CancellationToken cancellationToken);")]
    [InlineData("global::Consumer.Model.Mode Read();")]
    [InlineData("T Read<T>();")]
    public void An_allowed_shape_is_not_reported(string member)
    {
        Assert.Empty(Contract(member));
    }

    [Fact]
    public void A_task_of_a_type_parameter_is_not_reported()
    {
        // The rule looks through Task<>, and what it finds is whatever the caller supplies.
        Assert.Empty(Contract("System.Threading.Tasks.Task<TModel> Read<TModel>();"));
    }

    [Fact]
    public void A_model_type_on_a_contract_is_not_reported()
    {
        Assert.Empty(Contract("global::Consumer.Model.Position Read();"));
    }

    [Theory]
    [InlineData("public static Position Parse(string text) => default;")]
    [InlineData("public static bool TryParse(string text, out Position value) { value = default; return false; }")]
    [InlineData("public string Format(string pattern) => string.Empty;")]
    [InlineData("public static Position Of(string text) => default;")]
    public void A_boundary_member_on_the_wrapper_is_not_reported(string member)
    {
        // PrimitiveFreeSurfaces.BoundaryMembersExempt. Primitives come in somewhere, and the
        // wrapper's own parse, format and factory members are where.
        Assert.Empty(Model("public readonly record struct Position(long Value) { " + member + " }"));
    }

    [Fact]
    public void A_wrappers_own_primitive_is_not_reported_on_its_members()
    {
        // Position(long) and Position.Value. Without this the rule asks for a type nobody can build.
        Assert.Empty(Model("public readonly record struct Position(long Value);"));
    }

    [Fact]
    public void A_different_primitive_on_the_wrapper_is_still_reported()
    {
        // Position wraps a long; that is no reason for it to hand back a Guid.
        Diagnostic diagnostic = Assert.Single(Model(
            "public readonly record struct Position(long Value) { public System.Guid Other => default; }"));

        Assert.Contains("'System.Guid'", diagnostic.GetMessage(), StringComparison.Ordinal);
    }

    [Fact]
    public void A_span_parsable_implementation_is_not_reported()
    {
        Assert.Empty(Model(
            "public readonly record struct Code : System.ISpanParsable<Code> { "
            + "public static Code Parse(System.ReadOnlySpan<char> s, System.IFormatProvider? p) => default; "
            + "public static bool TryParse(System.ReadOnlySpan<char> s, System.IFormatProvider? p, out Code r) { r = default; return false; } "
            + "public static Code Parse(string s, System.IFormatProvider? p) => default; "
            + "public static bool TryParse(string? s, System.IFormatProvider? p, out Code r) { r = default; return false; } }"));
    }

    [Fact]
    public void A_utf8_span_formattable_implementation_is_not_reported()
    {
        Assert.Empty(Model(
            "public readonly record struct Code : System.IUtf8SpanFormattable { "
            + "public bool TryFormat(System.Span<byte> utf8Destination, out int bytesWritten, "
            + "System.ReadOnlySpan<char> format, System.IFormatProvider? provider) "
            + "{ bytesWritten = 0; return false; } }"));
    }

    [Fact]
    public void A_hot_path_member_is_not_reported()
    {
        Assert.Empty(Contract("[global::DecisionDriven.HotPath(" + ContractSource.Decision + ")] long Read();"));
    }

    [Fact]
    public void A_member_marked_with_DesignDecision_is_not_reported()
    {
        Assert.Empty(Contract(
            "[global::DecisionDriven.DesignDecision(" + ContractSource.Decision
            + ", Scope = global::DecisionDriven.ExceptionScope.Boundary)] long Read();"));
    }

    [Fact]
    public void A_public_member_of_a_model_type_is_in_scope()
    {
        Assert.Single(Model("public sealed class Product { public string Name => string.Empty; }"));
    }

    [Fact]
    public void A_non_public_member_of_a_model_type_is_not_in_scope()
    {
        // The rest of a model type is how it is built, not what it says.
        Assert.Empty(Model("public sealed class Product { internal string Name => string.Empty; }"));
    }

    [Fact]
    public void A_type_outside_the_model_and_outside_a_contract_is_not_in_scope()
    {
        Assert.Empty(Run("namespace Consumer.Scratch { public sealed class Thing { public long Value => 0; } }"));
    }

    [Fact]
    public void The_editorconfig_option_adds_a_type_to_the_banned_list()
    {
        Assert.Empty(Contract("global::Consumer.Model.Mode Read();"));

        Assert.Single(Contract(
            "global::Consumer.Model.Mode Read();",
            Option("Consumer.Model.Mode")));
    }

    [Fact]
    public void An_option_naming_a_type_already_banned_changes_nothing()
    {
        // PrimitiveFreeSurfaces.BannedPrimitiveListIsAdditiveOnly. Adding string to a list that
        // already has string is a line that does nothing, which is the correct amount.
        Assert.Single(Contract("string Read();"));
        Assert.Single(Contract("string Read();", Option("string")));
        Assert.Single(Contract("long Read();", Option("string")));
    }

    [Theory]
    [InlineData("Guid")]
    [InlineData("System.Guid")]
    [InlineData("")]
    [InlineData("long")]
    public void No_option_can_make_string_silent(string configured)
    {
        // The option that could shorten the list would be a suppression path around DD0013 with no
        // citation anywhere: one .editorconfig line unbanning string across a repository, which is
        // what DecisionsAsTypes.NoPragmaOrSuppressMessage exists to stop.
        Assert.Single(Contract("string Read();", Option(configured)));
    }

    private static Dictionary<string, string> Option(string value) =>
        new Dictionary<string, string>(StringComparer.Ordinal) { [BannedPrimitivesOption] = value };

    [Fact]
    public void The_message_is_exactly_this()
    {
        // DiagnosticMessages.ExactMessageTested.
        Assert.Equal(
            "the return type of 'IQuadSource.Read' is 'long'. "
            + "Decide: give 'long' a name: a readonly record struct wrapping it, so that two of them "
            + "cannot be swapped and the surface says which is which "
            + "| mark it [DesignDecision(typeof(<Set>.<Key>), Scope = ExceptionScope.<Scope>)] "
            + "citing the accepted decision that says so. "
            + "Do not add the attribute without a decision that answers this; if the reason is only that "
            + "the code already looked like this, take the design change.",
            Assert.Single(Contract("long Read();")).GetMessage());
    }

    [Fact]
    public void A_written_type_that_is_not_the_primitive_names_both()
    {
        // "Task<string>, which is 'string'" - the reader has to find the string, and the written
        // type is what is actually on the line they are looking at.
        Diagnostic diagnostic = Assert.Single(Contract("System.Threading.Tasks.Task<string> Read();"));

        Assert.Contains(
            "is 'System.Threading.Tasks.Task<string>', which is 'string'",
            diagnostic.GetMessage(),
            StringComparison.Ordinal);
    }

    [Fact]
    public void Two_positions_are_reported_at_each()
    {
        Assert.Equal(2, Contract("long Read(long position);").Length);
    }

    private const string BannedPrimitivesOption = "dd_banned_primitive_types_add";

    private static ImmutableArray<Diagnostic> Contract(string member, Dictionary<string, string>? options = null) =>
        Run(
            "namespace Consumer { " + ContractSource.Contract
            + " public interface IQuadSource { " + member + " } }"
            + Environment.NewLine
            + "namespace Consumer.Model { public enum Mode { One, Two } public readonly record struct Position(long Value); }",
            options);

    private static ImmutableArray<Diagnostic> Model(string body, Dictionary<string, string>? options = null) =>
        Run("namespace Consumer.Model { " + body + " }", options);

    private static ImmutableArray<Diagnostic> Run(string source, Dictionary<string, string>? options = null) =>
        RuleHarness.Run(
            new NakedPrimitiveAnalyzer(),
            ContractSource.File(
                source,
                "[assembly: global::DecisionDriven.DomainModel(\"Consumer.Model\", " + ContractSource.Decision + ")]"),
            assemblyName: "Consumer",
            archLayer: 1,
            editorConfig: options);
}
