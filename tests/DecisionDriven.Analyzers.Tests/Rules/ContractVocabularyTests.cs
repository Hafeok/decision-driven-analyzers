using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Threading.Tasks;
using DecisionDriven.Analyzers.Rules;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Testing;
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

        // The design-change path names the namespace to declare, rather than saying "declare
        // [DomainModel]" and leaving the reader to work out on what.
        Assert.Contains(
            "[assembly: DomainModel(\"Consumer.Scratch\", typeof(<Set>.<Key>))]",
            diagnostic.GetMessage(),
            StringComparison.Ordinal);

        Assert.Contains(
            "mark 'Consumer.Scratch.Thing' itself [Contract(",
            diagnostic.GetMessage(),
            StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("public readonly struct Thing { }")]
    [InlineData("public readonly record struct Thing(long Value);")]
    [InlineData("public enum Thing { Stop, Continue }")]
    public void A_struct_or_enum_is_pointed_at_the_model_and_never_at_Contract(string declaration)
    {
        // ContractAttribute applies to interfaces, classes and delegates. Offering it for a struct
        // or an enum is a path that does not compile.
        string message = Assert.Single(Run(
            "global::Consumer.Scratch.Thing Read();",
            extra: "namespace Consumer.Scratch { " + declaration + " }",
            domainModel: "Consumer.Model")).GetMessage();

        Assert.DoesNotContain("[Contract(", message, StringComparison.Ordinal);
        Assert.Contains(
            "[assembly: DomainModel(\"Consumer.Scratch\", typeof(<Set>.<Key>))]",
            message,
            StringComparison.Ordinal);
    }

    [Fact]
    public void The_message_for_a_struct_of_this_assembly_is_exactly_this()
    {
        // DiagnosticMessages.ExactMessageTested.
        Diagnostic diagnostic = Assert.Single(Run(
            "void Read(global::Consumer.Scratch.Thing thing);",
            extra: "namespace Consumer.Scratch { public readonly struct Thing { } }",
            domainModel: "Consumer.Model"));

        Assert.Equal(
            "the parameter 'thing' of contract member 'IQuadSource.Read' names 'Consumer.Scratch.Thing', "
            + "which is declared in this assembly outside any [DomainModel] namespace. "
            + "Decide: move 'Consumer.Scratch.Thing' into a namespace already declared as model "
            + "| declare 'Consumer.Scratch' as model with "
            + "[assembly: DomainModel(\"Consumer.Scratch\", typeof(<Set>.<Key>))]; a struct or an enum is "
            + "data, and [Contract] does not apply to it. "
            + "Do not add the attribute without a decision that answers this; if the reason is only that "
            + "the code already looked like this, take the design change.",
            diagnostic.GetMessage());
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
            + "Decide: use a type this contract may already name, or take what it needs into this "
            + "assembly's [DomainModel] namespaces "
            + "| add 'Sample.Layer1.Extras' to ArchContractTypeAssemblies, in a decision that says why "
            + "every consumer of this contract now depends on 'Sample.Layer1.Extras'. "
            + "Do not add the attribute without a decision that answers this; if the reason is only that "
            + "the code already looked like this, take the design change.",
            diagnostic.GetMessage());
    }

    // A contract class built from engine state: its constructor is how the assembly makes it, and
    // nothing outside the assembly can call it or see the type it takes.
    private const string EngineThing = "namespace Consumer.Engine { public sealed class Thing { } }";

    [Fact]
    public void An_internal_constructor_of_a_contract_class_is_not_reported()
    {
        Assert.Empty(Run(
            string.Empty,
            extra: "namespace Consumer { " + ContractSource.Contract + " public sealed class View { "
                + "internal View(global::Consumer.Engine.Thing thing) { } public int Count => 0; } } " + EngineThing,
            domainModel: "Consumer.Model"));
    }

    [Fact]
    public void A_private_protected_member_of_a_contract_class_is_not_reported()
    {
        Assert.Empty(Run(
            string.Empty,
            extra: "namespace Consumer { " + ContractSource.Contract + " public abstract class View { "
                + "private protected View(global::Consumer.Engine.Thing thing) { } } } " + EngineThing,
            domainModel: "Consumer.Model"));
    }

    [Fact]
    public void A_protected_member_of_a_contract_class_is_reported()
    {
        // Protected is reachable from a derived type in another assembly: it is on the surface.
        Diagnostic diagnostic = Assert.Single(Run(
            string.Empty,
            extra: "namespace Consumer { " + ContractSource.Contract + " public abstract class View { "
                + "protected View(global::Consumer.Engine.Thing thing) { } } } " + EngineThing,
            domainModel: "Consumer.Model"));

        Assert.Equal("DD0010", diagnostic.Id);
    }

    // ArchContractTypeAssemblies as an MSBuild user writes it, read the way a build reads it: through
    // the analyzer config file CompilerVisibleProperty generates, and the compiler's parser for it.
    private const string ListedTwo = "A;B";

    [Fact]
    public async Task Every_assembly_in_a_list_read_from_an_analyzer_config_file_is_accepted()
    {
        await RuleHarness.VerifyThroughAnalyzerConfigAsync<ContractVocabularyAnalyzer>(
            ConfigPathSource("global::A.First First(); global::B.Second Second();"),
            ConfigPathProperties(),
            ConfigPathReferences());
    }

    [Fact]
    public async Task An_assembly_missing_from_a_list_read_from_an_analyzer_config_file_is_reported_with_the_exact_message()
    {
        // DiagnosticMessages.ExactMessageTested.
        await RuleHarness.VerifyThroughAnalyzerConfigAsync<ContractVocabularyAnalyzer>(
            ConfigPathSource("global::A.First First(); global::B.Second Second(); global::C.Third {|#0:Third|}();"),
            ConfigPathProperties(),
            ConfigPathReferences(),
            new DiagnosticResult("DD0010", DiagnosticSeverity.Error)
                .WithLocation(0)
                .WithMessage(
                    "the return type of contract member 'IQuadSource.Third' names 'C.Third', "
                    + "which comes from 'C', which is not in ArchContractTypeAssemblies. "
                    + "Decide: use a type this contract may already name, or take what it needs into this "
                    + "assembly's [DomainModel] namespaces "
                    + "| add 'C' to ArchContractTypeAssemblies, in a decision that says why "
                    + "every consumer of this contract now depends on 'C'. "
                    + "Do not add the attribute without a decision that answers this; if the reason is only that "
                    + "the code already looked like this, take the design change."));
    }

    private static string ConfigPathSource(string members) =>
        ContractSource.File(
            "namespace Consumer { " + ContractSource.Contract + " public interface IQuadSource { " + members + " } }");

    private static Dictionary<string, string> ConfigPathProperties() => new(StringComparer.Ordinal)
    {
        ["ArchLayer"] = "1",
        ["ArchContractTypeAssemblies"] = ListedTwo,
    };

    private static IEnumerable<RuleHarness.Referenced> ConfigPathReferences() => new[]
    {
        new RuleHarness.Referenced("A", archLayer: 0, "namespace A { public sealed class First { } }"),
        new RuleHarness.Referenced("B", archLayer: 0, "namespace B { public sealed class Second { } }"),
        new RuleHarness.Referenced("C", archLayer: 0, "namespace C { public sealed class Third { } }"),
    };

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
