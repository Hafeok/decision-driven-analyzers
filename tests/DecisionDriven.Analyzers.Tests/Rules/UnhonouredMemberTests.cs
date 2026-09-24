using System;
using System.Collections.Immutable;
using DecisionDriven.Analyzers.Rules;
using Microsoft.CodeAnalysis;
using Xunit;

namespace DecisionDriven.Analyzers.Tests.Rules;

/// <summary>
/// DD0012: a type does not claim a member it does not honour.
/// </summary>
public sealed class UnhonouredMemberTests
{
    private const string Interface = "public interface IQuadSource { int Read(); int Count { get; } }";

    [Fact]
    public void An_expression_bodied_throw_in_an_implementation_is_reported()
    {
        Diagnostic diagnostic = Assert.Single(Run(
            Interface + " public sealed class Empty : IQuadSource { "
            + "public int Read() => throw new System.NotSupportedException(); "
            + "public int Count => 0; }"));

        Assert.Equal("DD0012", diagnostic.Id);
        Assert.Equal(DiagnosticSeverity.Error, diagnostic.Severity);
        Assert.Contains("implements 'Consumer.IQuadSource.Read'", diagnostic.GetMessage(), StringComparison.Ordinal);
    }

    [Fact]
    public void A_block_whose_only_statement_is_the_throw_is_reported()
    {
        Assert.Single(Run(
            Interface + " public sealed class Empty : IQuadSource { "
            + "public int Read() { throw new System.NotSupportedException(); } "
            + "public int Count => 0; }"));
    }

    [Fact]
    public void A_throw_inside_an_if_inside_an_implementation_is_reported()
    {
        // A refusal behind a condition is the same claim with a condition on it, and the harder
        // one to find by reading.
        Assert.Single(Run(
            Interface + " public sealed class Empty : IQuadSource { "
            + "public int Read() { if (Count == 0) { throw new System.NotSupportedException(); } return 1; } "
            + "public int Count => 0; }"));
    }

    [Fact]
    public void The_same_throw_in_a_method_that_implements_nothing_is_not_reported()
    {
        // No contract said otherwise. A method that throws is a method that throws.
        Assert.Empty(Run(
            "public sealed class Empty { public int Scratch() => throw new System.NotSupportedException(); }"));
    }

    [Fact]
    public void A_member_marked_with_DesignDecision_is_not_reported()
    {
        Assert.Empty(Run(
            Interface + " public sealed class Empty : IQuadSource { "
            + "[global::DecisionDriven.DesignDecision(" + ContractSource.Decision
            + ", Scope = global::DecisionDriven.ExceptionScope.Compatibility)] "
            + "public int Read() => throw new System.NotSupportedException(); "
            + "public int Count => 0; }"));
    }

    [Fact]
    public void An_override_that_throws_is_reported()
    {
        Diagnostic diagnostic = Assert.Single(Run(
            "public abstract class Source { public abstract int Read(); } "
            + "public sealed class Empty : Source { public override int Read() => throw new System.NotSupportedException(); }"));

        Assert.Contains("overrides a member it does not honour", diagnostic.GetMessage(), StringComparison.Ordinal);
    }

    [Fact]
    public void An_explicit_implementation_that_throws_is_reported()
    {
        Diagnostic diagnostic = Assert.Single(Run(
            Interface + " public sealed class Empty : IQuadSource { "
            + "int IQuadSource.Read() => throw new System.NotSupportedException(); "
            + "int IQuadSource.Count => 0; }"));

        Assert.Contains("explicitly implements 'Consumer.IQuadSource'", diagnostic.GetMessage(), StringComparison.Ordinal);
    }

    [Fact]
    public void A_property_accessor_that_throws_is_reported_as_the_property()
    {
        // The claim being broken is the property's. Naming the getter would name a member nobody
        // wrote.
        Diagnostic diagnostic = Assert.Single(Run(
            Interface + " public sealed class Empty : IQuadSource { "
            + "public int Read() => 1; "
            + "public int Count => throw new System.NotSupportedException(); }"));

        Assert.Contains("'Consumer.Empty.Count' throws", diagnostic.GetMessage(), StringComparison.Ordinal);
    }

    [Fact]
    public void Another_exception_type_is_not_this_rules_finding()
    {
        // DD0018 is where NotImplementedException gets its own answer.
        Assert.Empty(Run(
            Interface + " public sealed class Empty : IQuadSource { "
            + "public int Read() => throw new System.NotImplementedException(); "
            + "public int Count => 0; }"));
    }

    [Fact]
    public void A_test_assembly_is_not_checked()
    {
        Assert.Empty(Run(
            Interface + " public sealed class Empty : IQuadSource { "
            + "public int Read() => throw new System.NotSupportedException(); "
            + "public int Count => 0; }",
            assemblyName: "Consumer.Tests"));
    }

    [Fact]
    public void The_message_is_exactly_this()
    {
        // DiagnosticMessages.ExactMessageTested.
        Diagnostic diagnostic = Assert.Single(Run(
            Interface + " public sealed class Empty : IQuadSource { "
            + "public int Read() => throw new System.NotSupportedException(); "
            + "public int Count => 0; }"));

        Assert.Equal(
            "'Consumer.Empty.Read' throws NotSupportedException, and implements 'Consumer.IQuadSource.Read'. "
            + "Decide: split the interface so that this type only claims what it honours, or stop "
            + "implementing the member "
            + "| mark it [DesignDecision(typeof(<Set>.<Key>), Scope = ExceptionScope.<Scope>)] "
            + "citing the accepted decision that says so. "
            + "Do not add the attribute without a decision that answers this; if the reason is only that "
            + "the code already looked like this, take the design change.",
            diagnostic.GetMessage());
    }

    private static ImmutableArray<Diagnostic> Run(string body, string assemblyName = "Consumer") =>
        RuleHarness.Run(
            new UnhonouredMemberAnalyzer(),
            ContractSource.File("namespace Consumer { " + body + " }"),
            assemblyName: assemblyName,
            archLayer: 1);
}
