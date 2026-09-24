using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using DecisionDriven.Analyzers.Rules;
using Microsoft.CodeAnalysis;
using Xunit;

namespace DecisionDriven.Analyzers.Tests.Rules;

/// <summary>
/// DD0010: a contract names only types its consumers already agreed to.
/// </summary>
/// <remarks>
/// The allowed and disallowed assemblies are compiled for real and referenced as metadata, because
/// the rule's question is which assembly a type came from and a source-only test has one assembly.
/// </remarks>
public sealed class ContractVocabularyTests
{
    private const string Listed = "Sample.Layer0";
    private const string Unlisted = "Sample.Layer1.Extras";

    [Fact]
    public void A_type_from_a_listed_assembly_is_not_reported()
    {
        Assert.Empty(Run("global::Sample.Layer0.Quad Read();"));
    }

    [Fact]
    public void A_type_from_an_assembly_that_is_not_listed_is_reported()
    {
        Diagnostic diagnostic = Assert.Single(Run("global::Sample.Layer1.Extras.Scratch Read();"));

        Assert.Equal("DD0010", diagnostic.Id);
        Assert.Contains(
            "names 'Sample.Layer1.Extras.Scratch', which comes from 'Sample.Layer1.Extras', which is not in ArchContractTypeAssemblies",
            diagnostic.GetMessage(),
            StringComparison.Ordinal);
    }

    [Fact]
    public void A_BCL_type_is_not_reported()
    {
        Assert.Empty(Run("System.Collections.Generic.IReadOnlyList<int> Read(System.DateTimeOffset at);"));
    }

    [Fact]
    public void A_domain_model_type_of_this_assembly_is_not_reported()
    {
        Assert.Empty(Run(
            "global::Consumer.Model.Quad Read();",
            extra: "namespace Consumer.Model { public sealed class Quad { } }",
            domainModel: "Consumer.Model"));
    }

    [Fact]
    public void A_type_of_this_assembly_outside_a_domain_model_namespace_is_reported()
    {
        Diagnostic diagnostic = Assert.Single(Run(
            "global::Consumer.Scratch.Thing Read();",
            extra: "namespace Consumer.Scratch { public sealed class Thing { } }",
            domainModel: "Consumer.Model"));

        Assert.Contains(
            "is declared in this assembly outside any [DomainModel] namespace",
            diagnostic.GetMessage(),
            StringComparison.Ordinal);
    }

    [Fact]
    public void A_contract_marked_type_is_not_reported()
    {
        Assert.Empty(Run(
            "global::Consumer.Scratch.IThing Read();",
            extra: "namespace Consumer.Scratch { " + ContractSource.Contract + " public interface IThing { } }"));
    }

    [Fact]
    public void A_generic_argument_is_checked_like_anything_else()
    {
        // Task<Undecided> exposes the undecided type exactly as plainly as returning it would.
        Diagnostic diagnostic = Assert.Single(Run(
            "System.Collections.Generic.IReadOnlyList<global::Sample.Layer1.Extras.Scratch> Read();"));

        Assert.Contains("'Sample.Layer1.Extras.Scratch'", diagnostic.GetMessage(), StringComparison.Ordinal);
    }

    [Fact]
    public void A_parameter_is_checked_like_a_return_type()
    {
        Diagnostic diagnostic = Assert.Single(Run("int Read(global::Sample.Layer1.Extras.Scratch scratch);"));

        Assert.Contains("the parameter 'scratch' of contract member", diagnostic.GetMessage(), StringComparison.Ordinal);
    }

    [Fact]
    public void A_listed_generic_argument_is_not_reported()
    {
        Assert.Empty(Run("System.Collections.Generic.IReadOnlyList<global::Sample.Layer0.Quad> Read();"));
    }

    [Fact]
    public void One_type_named_twice_in_one_position_is_reported_once()
    {
        // One caret, one edit, one sentence. Saying "Scratch is not allowed" twice about the same
        // return type would be the message repeating itself with more brackets.
        Assert.Single(Run(
            "System.Collections.Generic.IReadOnlyDictionary<global::Sample.Layer1.Extras.Scratch, global::Sample.Layer1.Extras.Scratch> Read();"));
    }

    [Fact]
    public void One_type_in_two_positions_is_reported_at_each()
    {
        // Two places to edit, so two carets. Collapsing them would send somebody to fix the return
        // type and leave the parameter for the next build.
        Assert.Equal(
            2,
            Run("global::Sample.Layer1.Extras.Scratch Read(global::Sample.Layer1.Extras.Scratch other);").Length);
    }

    [Fact]
    public void A_type_that_is_not_a_contract_is_not_checked()
    {
        // DD0010 reads contract signatures. An unmarked public interface is DD0009's finding, and
        // reporting its signature too would report the same omission twice.
        Assert.Empty(RuleHarness.Run(
            new ContractVocabularyAnalyzer(),
            ContractSource.File(
                "namespace Consumer { public interface IQuadSource { global::Sample.Layer1.Extras.Scratch Read(); } }"),
            assemblyName: "Consumer",
            archLayer: 1,
            contractTypeAssemblies: Listed,
            references: References()));
    }

    [Fact]
    public void The_message_is_exactly_this()
    {
        // DiagnosticMessages.ExactMessageTested.
        Diagnostic diagnostic = Assert.Single(Run("global::Sample.Layer1.Extras.Scratch Read();"));

        Assert.Equal(
            "the return type of contract member 'IQuadSource.Read' names 'Sample.Layer1.Extras.Scratch', "
            + "which comes from 'Sample.Layer1.Extras', which is not in ArchContractTypeAssemblies. "
            + "Decide: use a type the contract may already name, or add 'Sample.Layer1.Extras' to "
            + "ArchContractTypeAssemblies in a decision that says why every consumer of this contract "
            + "now depends on it "
            + "| mark it [DesignDecision(typeof(<Set>.<Key>), Scope = ExceptionScope.<Scope>)] "
            + "citing the accepted decision that says so. "
            + "Do not add the attribute without a decision that answers this; if the reason is only that "
            + "the code already looked like this, take the design change.",
            diagnostic.GetMessage());
    }

    private static ImmutableArray<Diagnostic> Run(string members, string extra = "", string? domainModel = null)
    {
        string assemblyAttributes = domainModel is null
            ? string.Empty
            : "[assembly: global::DecisionDriven.DomainModel(\"" + domainModel + "\", " + ContractSource.Decision + ")]";

        string source = "namespace Consumer { "
            + ContractSource.Contract + " public interface IQuadSource { " + members + " } }"
            + Environment.NewLine + extra;

        return RuleHarness.Run(
            new ContractVocabularyAnalyzer(),
            ContractSource.File(source, assemblyAttributes),
            assemblyName: "Consumer",
            archLayer: 1,
            contractTypeAssemblies: Listed,
            references: References());
    }

    private static IEnumerable<RuleHarness.Referenced> References() => new[]
    {
        new RuleHarness.Referenced(Listed, archLayer: 0, "namespace Sample.Layer0 { public sealed class Quad { } }"),
        new RuleHarness.Referenced(Unlisted, archLayer: 0, "namespace Sample.Layer1.Extras { public sealed class Scratch { } }"),
    };
}
