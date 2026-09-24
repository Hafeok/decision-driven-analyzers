using System;
using System.Collections.Immutable;
using DecisionDriven.Analyzers.Rules;
using Microsoft.CodeAnalysis;
using Xunit;

namespace DecisionDriven.Analyzers.Tests.Rules;

/// <summary>
/// DD0011: a contract parameter is the data a member works on, not somebody to work with.
/// </summary>
public sealed class ContractParameterTests
{
    [Theory]
    [InlineData("System.ReadOnlySpan<byte> bytes")]
    [InlineData("System.Memory<byte> bytes")]
    [InlineData("System.Threading.CancellationToken cancellationToken")]
    [InlineData("global::Consumer.Mode mode")]
    [InlineData("System.Func<int, int> project")]
    [InlineData("global::Consumer.Callback callback")]
    [InlineData("int count")]
    [InlineData("global::Consumer.Model.Quad quad")]
    public void Data_parameters_are_not_reported(string parameter)
    {
        Assert.Empty(Run("void Read(" + parameter + ");"));
    }

    [Fact]
    public void A_type_parameter_is_not_reported()
    {
        // Whatever the caller supplies, and the caller's own contract is where that is decided.
        Assert.Empty(Run("void Read<T>(T value);"));
    }

    [Fact]
    public void An_interface_parameter_that_no_decision_declares_a_contract_is_reported()
    {
        Diagnostic diagnostic = Assert.Single(Run(
            "void Read(global::Consumer.IClock clock);",
            extra: "namespace Consumer { public interface IClock { } }"));

        Assert.Equal("DD0011", diagnostic.Id);
        Assert.Equal(DiagnosticSeverity.Error, diagnostic.Severity);
        Assert.Contains("an interface no decision declares a contract", diagnostic.GetMessage(), StringComparison.Ordinal);
    }

    [Fact]
    public void An_abstract_class_parameter_that_no_decision_declares_a_contract_is_reported()
    {
        Diagnostic diagnostic = Assert.Single(Run(
            "void Read(global::Consumer.Clock clock);",
            extra: "namespace Consumer { public abstract class Clock { } }"));

        Assert.Contains("an abstract class no decision declares a contract", diagnostic.GetMessage(), StringComparison.Ordinal);
    }

    [Fact]
    public void An_object_parameter_is_reported()
    {
        // The same move with the type name taken off.
        Diagnostic diagnostic = Assert.Single(Run("void Read(object value);"));

        Assert.Contains("is 'object'", diagnostic.GetMessage(), StringComparison.Ordinal);
    }

    [Fact]
    public void A_contract_marked_interface_parameter_is_not_reported()
    {
        // A declared dependency rather than an ad-hoc one: the decision that made it a contract is
        // the decision that says passing it around is intended.
        Assert.Empty(Run(
            "void Read(global::Consumer.IClock clock);",
            extra: "namespace Consumer { " + ContractSource.Contract + " public interface IClock { } }"));
    }

    [Fact]
    public void A_BCL_interface_parameter_is_not_reported()
    {
        // The framework's interfaces are how it spells data. Reading the ADR's "interfaces are
        // errors" clause over its "types from the allowed vocabulary" clause would report every
        // sequence parameter in every contract.
        Assert.Empty(Run("void Read(System.Collections.Generic.IEnumerable<int> values);"));
    }

    [Fact]
    public void An_array_of_collaborators_is_reported()
    {
        // A collaborator with a loop around it.
        Assert.Single(Run(
            "void Read(global::Consumer.IClock[] clocks);",
            extra: "namespace Consumer { public interface IClock { } }"));
    }

    [Fact]
    public void A_collaborator_return_type_is_not_this_rules_finding()
    {
        // DD0011 is about what a caller has to supply. A returned interface is DD0010's question
        // about which package it comes from, and one rule asks one question.
        Assert.Empty(Run(
            "global::Consumer.IClock Read();",
            extra: "namespace Consumer { public interface IClock { } }"));
    }

    [Fact]
    public void The_message_is_exactly_this()
    {
        // DiagnosticMessages.ExactMessageTested.
        Diagnostic diagnostic = Assert.Single(Run(
            "void Read(global::Consumer.IClock clock);",
            extra: "namespace Consumer { public interface IClock { } }"));

        Assert.Equal(
            "parameter 'clock' of contract member 'IQuadSource.Read' is 'Consumer.IClock', "
            + "an interface no decision declares a contract. "
            + "Decide: take it through the constructor of the implementing type, or mark "
            + "'Consumer.IClock' [Contract(typeof(<Set>.<Key>), Role = \"...\")] so that passing it "
            + "is a declared dependency "
            + "| mark it [DesignDecision(typeof(<Set>.<Key>), Scope = ExceptionScope.<Scope>)] "
            + "citing the accepted decision that says so. "
            + "Do not add the attribute without a decision that answers this; if the reason is only that "
            + "the code already looked like this, take the design change.",
            diagnostic.GetMessage());
    }

    private static ImmutableArray<Diagnostic> Run(string members, string extra = "") =>
        RuleHarness.Run(
            new ContractParameterAnalyzer(),
            ContractSource.File(
                "namespace Consumer { "
                + ContractSource.Contract + " public interface IQuadSource { " + members + " } "
                + "public enum Mode { One, Two } "
                + "public delegate void Callback(int value); }"
                + Environment.NewLine
                + "namespace Consumer.Model { public sealed class Quad { } }"
                + Environment.NewLine + extra),
            assemblyName: "Consumer",
            archLayer: 1);
}
