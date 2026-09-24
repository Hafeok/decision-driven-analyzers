using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using DecisionDriven.Analyzers.Rules;
using DecisionDriven.Analyzers.Tests.Ledger;
using Microsoft.CodeAnalysis;
using Xunit;

namespace DecisionDriven.Analyzers.Tests.Rules;

/// <summary>
/// DD0007: a citation names a decision the ledger produced, and carries its required arguments.
/// </summary>
/// <remarks>
/// Every case runs the real generator first. The rule's whole subject is the difference between a
/// type the generator emitted and one that only looks like it, so a test that hand-rolled the
/// generated source would be testing the two things the rule distinguishes as if they were one.
/// </remarks>
public sealed class DecisionCitationTests
{
    private const string Set = "sample-set";
    private const string Key = "QuadSourceContract";
    private const string Cited = "global::DecisionDriven.Ledger.SampleNs.SampleSet.QuadSourceContract";

    [Fact]
    public void A_contract_citing_a_generated_decision_is_not_reported()
    {
        Assert.Empty(Run(
            "namespace Consumer { " + Contract(Cited, "\"store read side\"") + " public interface IQuadSource { } }"));
    }

    [Fact]
    public void A_hot_path_citing_a_generated_decision_is_not_reported()
    {
        Assert.Empty(Run(
            "namespace Consumer { public sealed class Thing { "
            + "[global::DecisionDriven.HotPath(typeof(" + Cited + "))] public void Step() { } } }"));
    }

    [Fact]
    public void A_citation_of_a_hand_written_lookalike_is_reported()
    {
        // The point of the rule. This type has the emitted shape down to the three constants and
        // the namespace, and nothing generated it.
        Diagnostic diagnostic = Assert.Single(Run(
            Lookalike(generatedCode: false)
            + "namespace Consumer { "
            + Contract("global::DecisionDriven.Ledger.SampleNs.Forged.NotFromTheLedger", "\"store read side\"")
            + " public interface IQuadSource { } }"));

        Assert.Equal("DD0007", diagnostic.Id);
        Assert.Contains("did not come out of a generator run", diagnostic.GetMessage(), System.StringComparison.Ordinal);
    }

    [Fact]
    public void A_lookalike_that_copies_the_generated_code_attribute_is_still_reported()
    {
        // The attribute is text, and anyone can type text. It is required because a file that does
        // not claim to be generated is not, and it is not sufficient because claiming costs
        // nothing. What this file cannot have is the path Roslyn gives generator output.
        Diagnostic diagnostic = Assert.Single(Run(
            Lookalike(generatedCode: true)
            + "namespace Consumer { "
            + Contract("global::DecisionDriven.Ledger.SampleNs.Forged.NotFromTheLedger", "\"store read side\"")
            + " public interface IQuadSource { } }"));

        Assert.Equal("DD0007", diagnostic.Id);
        Assert.Contains("did not come out of a generator run", diagnostic.GetMessage(), System.StringComparison.Ordinal);
    }

    [Fact]
    public void The_generator_emits_both_signals()
    {
        // The rule reads what the generator writes. If these two drifted apart, every citation in
        // every consumer would be reported and the tests above would still pass.
        GeneratorHarness.Result result = Generate("namespace Consumer { }");

        string source = Assert.IsType<string>(result.GeneratedSource("DecisionDriven.Ledger.SampleNs.g.cs"));
        Assert.Contains(
            "[global::System.CodeDom.Compiler.GeneratedCode(\"DecisionDriven.Analyzers\", ",
            source,
            System.StringComparison.Ordinal);

        SyntaxTree tree = Assert.Single(
            result.Compilation.SyntaxTrees,
            t => t.FilePath.EndsWith("DecisionDriven.Ledger.SampleNs.g.cs", System.StringComparison.Ordinal));

        Assert.Contains(
            "DecisionDriven.Analyzers.DecisionLedgerGenerator",
            tree.FilePath,
            System.StringComparison.Ordinal);
    }

    [Fact]
    public void A_citation_of_a_type_that_is_not_a_decision_at_all_is_reported()
    {
        Diagnostic diagnostic = Assert.Single(Run(
            "namespace Consumer { " + Contract("string", "\"store read side\"") + " public interface IQuadSource { } }"));

        Assert.Equal("DD0007", diagnostic.Id);
        Assert.Contains("is not a decision type", diagnostic.GetMessage(), System.StringComparison.Ordinal);
    }

    [Fact]
    public void A_citation_of_a_decision_that_does_not_exist_is_left_to_the_compiler()
    {
        // CS0246 on the same token, one line earlier in the build output than DD0007 would be. Two
        // diagnostics about one misspelling teach a reader to skim one of them.
        GeneratorHarness.Result result = Generate(
            "namespace Consumer { " + Contract("global::DecisionDriven.Ledger.SampleNs.SampleSet.NoSuchKey", "\"x\"") + " public interface IQuadSource { } }");

        Assert.Contains(
            result.CompilationDiagnostics,
            d => d.Severity == DiagnosticSeverity.Error && d.GetMessage().Contains("NoSuchKey", System.StringComparison.Ordinal));

        Assert.Empty(RuleHarness
            .RunOn(new DecisionCitationAnalyzer(), result.Compilation, allowCompileErrors: true)
            .Where(d => d.Id == "DD0007"));
    }

    [Fact]
    public void A_contract_without_a_role_is_reported()
    {
        Diagnostic diagnostic = Assert.Single(Run(
            "namespace Consumer { [global::DecisionDriven.Contract(typeof(" + Cited + "))] public interface IQuadSource { } }"));

        Assert.Equal("DD0007", diagnostic.Id);
        Assert.Contains("does not set Role", diagnostic.GetMessage(), System.StringComparison.Ordinal);
    }

    [Fact]
    public void A_contract_with_an_empty_role_is_reported()
    {
        // The omission with extra characters. Role is the label the report tool prints next to the
        // contract, and "" prints as nothing.
        Diagnostic diagnostic = Assert.Single(Run(
            "namespace Consumer { " + Contract(Cited, "\"   \"") + " public interface IQuadSource { } }"));

        Assert.Equal("DD0007", diagnostic.Id);
        Assert.Contains("empty string", diagnostic.GetMessage(), System.StringComparison.Ordinal);
    }

    [Fact]
    public void A_design_decision_without_a_scope_is_reported()
    {
        Diagnostic diagnostic = Assert.Single(Run(
            "namespace Consumer { public sealed class Thing { "
            + "[global::DecisionDriven.DesignDecision(typeof(" + Cited + "))] public int Field; } }"));

        Assert.Equal("DD0007", diagnostic.Id);
        Assert.Contains("does not set Scope", diagnostic.GetMessage(), System.StringComparison.Ordinal);
    }

    [Fact]
    public void A_design_decision_with_a_scope_is_not_reported()
    {
        Assert.Empty(Run(
            "namespace Consumer { public sealed class Thing { "
            + "[global::DecisionDriven.DesignDecision(typeof(" + Cited + "), Scope = global::DecisionDriven.ExceptionScope.Pool)] "
            + "public int Field; } }"));
    }

    [Fact]
    public void A_domain_model_prefix_this_assembly_declares_is_not_reported()
    {
        Assert.Empty(Run(
            "[assembly: global::DecisionDriven.DomainModel(\"Consumer.Model\", typeof(" + Cited + "))]"
            + " namespace Consumer.Model { public sealed class Quad { } }"));
    }

    [Fact]
    public void A_domain_model_prefix_this_assembly_does_not_declare_is_reported()
    {
        // Read from the assembly's own namespace tree, not the compilation's: System is a namespace
        // of every compilation and of no consumer.
        Diagnostic diagnostic = Assert.Single(Run(
            "[assembly: global::DecisionDriven.DomainModel(\"System.Collections\", typeof(" + Cited + "))]"
            + " namespace Consumer { public sealed class Quad { } }"));

        Assert.Equal("DD0007", diagnostic.Id);
        Assert.Contains("has no such namespace", diagnostic.GetMessage(), System.StringComparison.Ordinal);
    }

    [Fact]
    public void One_wrong_attribute_is_reported_once()
    {
        // The citation and the missing Role are both wrong here. Fixing the citation is what brings
        // the Role into view; saying both at once says neither.
        Assert.Single(Run(
            "namespace Consumer { [global::DecisionDriven.Contract(typeof(string))] public interface IQuadSource { } }"));
    }

    [Fact]
    public void The_message_is_exactly_this()
    {
        // DiagnosticMessages.ExactMessageTested.
        Diagnostic diagnostic = Assert.Single(Run(
            "namespace Consumer { " + Contract("string", "\"store read side\"") + " public interface IQuadSource { } }"));

        Assert.Equal(
            "[Contract] cites 'string', which is not a decision type. "
            + "Decide: drop the [Contract] attribute, or cite a decision under DecisionDriven.Ledger.* "
            + "that the generator emitted "
            + "| file the decision in the ledger, export it, and cite the type the generator emits for it. "
            + "Do not add the attribute without a decision that answers this; if the reason is only that "
            + "the code already looked like this, take the design change.",
            diagnostic.GetMessage());
    }

    /// <summary>A type with the emitted shape, in the emitted namespace, that nothing emitted.</summary>
    private static string Lookalike(bool generatedCode)
    {
        string marker = generatedCode
            ? "[global::System.CodeDom.Compiler.GeneratedCode(\"DecisionDriven.Analyzers\", \"9.9.9\")] "
            : string.Empty;

        return "namespace DecisionDriven.Ledger.SampleNs { " + marker + "internal static class Forged { "
            + "public const string SetId = \"forged\"; "
            + marker + "public static class NotFromTheLedger { "
            + "public const string Id = \"dec:sample-ns/NotFromTheLedger\"; "
            + "public const string Key = \"NotFromTheLedger\"; "
            + "public const string Namespace = \"sample-ns\"; } } }";
    }

    private static string Contract(string decision, string role) =>
        "[global::DecisionDriven.Contract(typeof(" + decision + "), Role = " + role + ")]";

    private static ImmutableArray<Diagnostic> Run(string source) => Analyze(Generate(source));

    private static GeneratorHarness.Result Generate(string source) =>
        GeneratorHarness.Run(
            source,
            new List<GeneratorHarness.LedgerFile>
            {
                LedgerInput.AsSet(LedgerInput.FrontMatter(
                    Set,
                    Key,
                    "The store's read side is a contract",
                    acceptedBy: "mailto:someone@example.com")),
            });

    private static ImmutableArray<Diagnostic> Analyze(GeneratorHarness.Result result) =>
        RuleHarness.RunOn(new DecisionCitationAnalyzer(), result.Compilation)
            .Where(d => d.Id == "DD0007")
            .ToImmutableArray();
}
