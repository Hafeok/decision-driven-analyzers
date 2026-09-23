using System.Linq;
using Microsoft.CodeAnalysis;
using Xunit;

namespace DecisionDriven.Analyzers.Tests.Ledger;

/// <summary>
/// What the generator emits, and what a consumer can do with it.
/// </summary>
public sealed class GeneratedSourceTests
{
    private const string Empty = "internal sealed class Nothing { }";

    [Fact]
    public void The_two_input_forms_produce_identical_generated_source()
    {
        // DecisionsAsTypes.InterimFrontMatterUntilExport says the interim form maps one-to-one onto
        // the same model. If it did not, adopting the export would silently change what code cites.
        GeneratorHarness.Result fromFrontMatter = GeneratorHarness.Run(
            Empty,
            new[] { LedgerInput.AsSet(LedgerInput.FrontMatter("sample-set", "SomeKey", "A statement.")) });

        GeneratorHarness.Result fromExport = GeneratorHarness.Run(
            Empty,
            new[] { LedgerInput.AsExport(LedgerInput.NTriples("sample-set", "SomeKey", "A statement.")) });

        string? a = fromFrontMatter.GeneratedSource("DecisionDriven.Ledger.SampleNs.g.cs");
        string? b = fromExport.GeneratedSource("DecisionDriven.Ledger.SampleNs.g.cs");

        Assert.NotNull(a);
        Assert.Equal(a, b);
    }

    [Fact]
    public void An_unaccepted_decision_is_obsolete_as_a_warning()
    {
        // DecisionsAsTypes.UnacceptedEmitsWarningObsolete: citable on a branch, not shippable under
        // warnings as errors. Asserted by citing it rather than by matching the emitted text, so
        // the test is about what the compiler does with the attribute.
        GeneratorHarness.Result result = Cite("SomeKey", acceptedBy: null);

        Diagnostic obsolete = Assert.Single(
            result.CompilationDiagnostics.Where(d => d.Id == "CS0618"));

        Assert.Equal(DiagnosticSeverity.Warning, obsolete.Severity);
    }

    [Fact]
    public void An_accepted_decision_is_not_obsolete()
    {
        GeneratorHarness.Result result = Cite("SomeKey", acceptedBy: "mailto:someone@example.com");

        Assert.Empty(result.CompilationDiagnostics.Where(d => d.Id is "CS0612" or "CS0618" or "CS0619"));
    }

    [Fact]
    public void A_revoked_decision_without_a_successor_is_obsolete_as_an_error()
    {
        // DecisionsAsTypes.RevokedEmitsErrorObsolete. Citing a decision that no longer holds is not
        // a warning to be weighed; it does not compile.
        GeneratorHarness.Result result = Cite("SomeKey", acceptedBy: null, revokedAt: "2026-10-02T00:00:00Z");

        Diagnostic obsolete = Assert.Single(
            result.CompilationDiagnostics.Where(d => d.Id == "CS0619"));

        Assert.Equal(DiagnosticSeverity.Error, obsolete.Severity);
    }

    [Fact]
    public void A_superseded_decision_is_not_obsolete_at_all()
    {
        // DecisionsAsTypes.SupersessionIsNotObsolescence: the key carries to the successor and code
        // keeps compiling. The divergence between the cited version and the tip is the ledger's own
        // invalidation signal, and it is the report tool's job, not the compiler's.
        const string Ledger = "urn:ledger:ns#";
        const string Rdf = "http://www.w3.org/1999/02/22-rdf-syntax-ns#";

        string export = LedgerInput.NTriples("sample-set", "SomeKey", "The old one.", revokedAt: "2026-10-02T00:00:00Z")
            + "<dec:sample-ns/Successor> <" + Rdf + "type> <" + Ledger + "Decision> .\n"
            + "<dec:sample-ns/Successor> <" + Ledger + "namespace> \"sample-ns\" .\n"
            + "<urn:interim:sample-ns/Successor> <" + Rdf + "type> <" + Ledger + "DecisionVersion> .\n"
            + "<urn:interim:sample-ns/Successor> <" + Ledger + "ofDecision> <dec:sample-ns/Successor> .\n"
            + "<urn:interim:sample-ns/Successor> <" + Ledger + "set> \"sample-set\" .\n"
            + "<urn:interim:sample-ns/Successor> <" + Ledger + "key> \"Successor\" .\n"
            + "<urn:interim:sample-ns/Successor> <" + Ledger + "supersedes> <dec:sample-ns/SomeKey> .\n"
            + "<urn:acceptance:SomeKey> <" + Rdf + "type> <" + Ledger + "Acceptance> .\n"
            + "<urn:acceptance:SomeKey> <" + Ledger + "signsVersion> <urn:interim:sample-ns/SomeKey> .\n"
            + "<urn:acceptance:SomeKey> <" + Ledger + "acceptedBy> <mailto:someone@example.com> .\n";

        GeneratorHarness.Result result = GeneratorHarness.Run(
            CitationOf("SomeKey"),
            new[] { LedgerInput.AsExport(export) });

        Assert.Empty(result.CompilationDiagnostics.Where(d => d.Id is "CS0612" or "CS0618" or "CS0619"));
    }

    [Fact]
    public void Citing_a_decision_that_does_not_exist_does_not_compile()
    {
        // The point of DecisionsAsTypes.LedgerIsSourceGeneratorIsReadModel: a citation is a symbol
        // reference, so code cannot cite a decision nobody filed.
        GeneratorHarness.Result result = GeneratorHarness.Run(
            "internal sealed class Citing { private static readonly global::System.Type D = typeof(global::DecisionDriven.Ledger.SampleNs.NoSuchSet.NoSuchKey); }",
            new[] { LedgerInput.AsSet(LedgerInput.FrontMatter("sample-set", "SomeKey", "A statement.")) });

        Assert.Contains(
            result.CompilationDiagnostics,
            d => d.Severity == DiagnosticSeverity.Error && d.Id is "CS0234" or "CS0246" or "CS0426");
    }

    private static GeneratorHarness.Result Cite(string key, string? acceptedBy, string? revokedAt = null) =>
        GeneratorHarness.Run(
            CitationOf(key),
            new[] { LedgerInput.AsSet(LedgerInput.FrontMatter("sample-set", key, "A statement.", acceptedBy, revokedAt)) });

    private static string CitationOf(string key) =>
        "internal sealed class Citing { private static readonly global::System.Type D = typeof(global::DecisionDriven.Ledger.SampleNs.SampleSet." + key + "); }";
}
