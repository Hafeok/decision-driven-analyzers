using System;
using System.Collections.Immutable;
using DecisionDriven.Analyzers.Rules;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;
using Xunit;

namespace DecisionDriven.Analyzers.Tests.Rules;

/// <summary>
/// DD0014, DD0015 and DD0016: the wrapper's shape, its conversions, and flag arguments.
/// </summary>
public sealed class WrapperAndFlagTests
{
    [Fact]
    public void A_class_wrapping_one_primitive_is_reported()
    {
        // Every one of them allocates, which is the cost the wrapper existed to avoid.
        Diagnostic diagnostic = Assert.Single(Shape("public sealed class Position { public long Value { get; } }"));

        Assert.Equal("DD0014", diagnostic.Id);
        Assert.Equal(DiagnosticSeverity.Error, diagnostic.Severity);
        Assert.Contains("is a class, so every one of them allocates", diagnostic.GetMessage(), StringComparison.Ordinal);
    }

    [Fact]
    public void A_readonly_record_struct_is_not_reported()
    {
        Assert.Empty(Shape("public readonly record struct Position(long Value);"));
    }

    [Fact]
    public void A_struct_that_is_not_readonly_is_reported()
    {
        Diagnostic diagnostic = Assert.Single(Shape("public struct Position { public long Value { get; set; } }"));

        Assert.Contains("is not a readonly struct", diagnostic.GetMessage(), StringComparison.Ordinal);
    }

    [Fact]
    public void A_readonly_struct_without_value_equality_is_reported()
    {
        // The default ValueType.Equals exists and nobody chose it: it boxes, and it compares by
        // reflection or by bytes depending on the layout.
        Diagnostic diagnostic = Assert.Single(Shape(
            "public readonly struct Position { public Position(long value) { Value = value; } public long Value { get; } }"));

        Assert.Contains("has no value equality", diagnostic.GetMessage(), StringComparison.Ordinal);
    }

    [Fact]
    public void A_readonly_struct_implementing_IEquatable_is_not_reported()
    {
        Assert.Empty(Shape(
            "public readonly struct Position : System.IEquatable<Position> { "
            + "public Position(long value) { Value = value; } public long Value { get; } "
            + "public bool Equals(Position other) => Value == other.Value; "
            + "public override bool Equals(object? o) => o is Position p && Equals(p); "
            + "public override int GetHashCode() => Value.GetHashCode(); "
            + "public static bool operator ==(Position a, Position b) => a.Equals(b); "
            + "public static bool operator !=(Position a, Position b) => !a.Equals(b); }"));
    }

    [Fact]
    public void A_type_with_two_fields_is_not_a_wrapper()
    {
        Assert.Empty(Shape("public sealed class Range { public long Start { get; } public long End { get; } }"));
    }

    [Fact]
    public void A_type_outside_the_model_is_not_checked()
    {
        Assert.Empty(RuleHarness.Run(
            new WrapperShapeAnalyzer(),
            File("namespace Consumer.Scratch { public sealed class Position { public long Value { get; } } }"),
            assemblyName: "Consumer",
            archLayer: 1));
    }

    [Fact]
    public void The_wrapper_shape_message_is_exactly_this()
    {
        // DiagnosticMessages.ExactMessageTested.
        Assert.Equal(
            "'Consumer.Model.Position' wraps one 'long' and is a class, so every one of them allocates. "
            + "Decide: make it 'public readonly record struct Position(long Value)', or a readonly "
            + "struct implementing IEquatable<T> with == and != "
            + "| mark it [DesignDecision(typeof(<Set>.<Key>), Scope = ExceptionScope.<Scope>)] "
            + "citing the accepted decision that says so. "
            + "Do not add the attribute without a decision that answers this; if the reason is only that "
            + "the code already looked like this, take the design change.",
            Assert.Single(Shape("public sealed class Position { public long Value { get; } }")).GetMessage());
    }

    [Theory]
    [InlineData("public static implicit operator long(Position p) => p.Value;", "to 'long'")]
    [InlineData("public static implicit operator Position(long v) => new Position(v);", "from 'long'")]
    public void An_implicit_conversion_is_reported(string member, string direction)
    {
        Diagnostic diagnostic = Assert.Single(Conversions(member));

        Assert.Equal("DD0015", diagnostic.Id);
        Assert.Contains("implicit conversion " + direction, diagnostic.GetMessage(), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("public static explicit operator long(Position p) => p.Value;")]
    [InlineData("public static explicit operator Position(long v) => new Position(v);")]
    public void An_explicit_conversion_is_not_reported(string member)
    {
        // It costs a word at the call site, and the word says which of the two this is.
        Assert.Empty(Conversions(member));
    }

    [Fact]
    public void A_named_accessor_is_not_reported()
    {
        Assert.Empty(Conversions("public long AsLong() => Value;"));
    }

    [Fact]
    public void The_implicit_conversion_message_is_exactly_this()
    {
        // DiagnosticMessages.ExactMessageTested.
        Assert.Equal(
            "'Consumer.Model.Position' has an implicit conversion to 'long', so the primitive and "
            + "the model type are interchangeable again. "
            + "Decide: make it 'explicit', or replace it with a named accessor such as '.Value' - "
            + "both cost a word at the call site, which is the word that says which of the two this is "
            + "| mark it [DesignDecision(typeof(<Set>.<Key>), Scope = ExceptionScope.<Scope>)] "
            + "citing the accepted decision that says so. "
            + "Do not add the attribute without a decision that answers this; if the reason is only that "
            + "the code already looked like this, take the design change.",
            Assert.Single(Conversions("public static implicit operator long(Position p) => p.Value;")).GetMessage());
    }

    [Fact]
    public void A_bool_parameter_is_warned_about()
    {
        Diagnostic diagnostic = Assert.Single(Flags("void Read(bool includeArchived);"));

        Assert.Equal("DD0016", diagnostic.Id);
        Assert.Equal(DiagnosticSeverity.Warning, diagnostic.Severity);
    }

    [Theory]
    [InlineData("bool Read();")]
    [InlineData("void Read(System.Func<bool> predicate);")]
    [InlineData("bool TryRead(out bool value);")]
    [InlineData("void Read(ref bool value);")]
    public void The_shapes_that_are_not_flags_are_silent(string member)
    {
        Assert.Empty(Flags(member));
    }

    [Fact]
    public void A_bool_parameter_on_a_public_model_member_is_warned_about()
    {
        Assert.Single(RuleHarness.Run(
            new FlagArgumentAnalyzer(),
            File("namespace Consumer.Model { public sealed class Catalog { public void Read(bool archived) { } } }"),
            assemblyName: "Consumer",
            archLayer: 1));
    }

    [Fact]
    public void A_member_marked_with_DesignDecision_is_not_warned_about()
    {
        Assert.Empty(Flags(
            "[global::DecisionDriven.DesignDecision(" + ContractSource.Decision
            + ", Scope = global::DecisionDriven.ExceptionScope.Compatibility)] void Read(bool archived);"));
    }

    [Fact]
    public void The_flag_argument_message_is_exactly_this()
    {
        // DiagnosticMessages.ExactMessageTested.
        Assert.Equal(
            "parameter 'includeArchived' of 'IQuadSource.Read' is a bool, so the call site reads "
            + "'Read(x, true)'. "
            + "Decide: give it an enum with two named members, or split the member in two "
            + "| mark it [DesignDecision(typeof(<Set>.<Key>), Scope = ExceptionScope.<Scope>)] "
            + "citing the accepted decision that says so. "
            + "Do not add the attribute without a decision that answers this; if the reason is only that "
            + "the code already looked like this, take the design change.",
            Assert.Single(Flags("void Read(bool includeArchived);")).GetMessage());
    }

    private static ImmutableArray<Diagnostic> Shape(string body) =>
        Run(new WrapperShapeAnalyzer(), "namespace Consumer.Model { " + body + " }");

    private static ImmutableArray<Diagnostic> Conversions(string member) =>
        Run(
            new ImplicitConversionAnalyzer(),
            "namespace Consumer.Model { public readonly record struct Position(long Value) { " + member + " } }");

    private static ImmutableArray<Diagnostic> Flags(string member) =>
        Run(
            new FlagArgumentAnalyzer(),
            "namespace Consumer { " + ContractSource.Contract + " public interface IQuadSource { " + member + " } }");

    private static ImmutableArray<Diagnostic> Run(DiagnosticAnalyzer analyzer, string source) =>
        RuleHarness.Run(analyzer, File(source), assemblyName: "Consumer", archLayer: 1);

    private static string File(string source) =>
        ContractSource.File(
            source,
            "[assembly: global::DecisionDriven.DomainModel(\"Consumer.Model\", " + ContractSource.Decision + ")]");
}
