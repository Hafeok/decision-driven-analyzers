using System;
using System.IO;
using System.Linq;
using Microsoft.CodeAnalysis;
using Xunit;

namespace DecisionDriven.Analyzers.Tests.Ledger;

/// <summary>
/// The N-Triples reader against an export in the shape the ledger writes.
/// </summary>
/// <remarks>
/// <c>Fixtures/export-v2.nt</c> is hand-built to the ruled export shape: <c>ledger:set</c> as an
/// IRI, every node typed twice as PROV-O requires, lines sorted by code point. The older shape the
/// <see cref="LedgerInput.NTriples"/> helper writes, a literal set id and one type per node, is still
/// read, and the tests that use it stay as they were.
/// </remarks>
public sealed class ExportShapeTests
{
    [Fact]
    public void The_set_id_is_the_local_part_of_the_ledger_set_IRI()
    {
        GeneratorHarness.Result result = Run(Citation("ExportedKey"), Fixture());

        string? source = result.GeneratedSource("DecisionDriven.Ledger.SampleNs.g.cs");

        Assert.NotNull(source);
        Assert.Contains("static class SampleSet", source, StringComparison.Ordinal);
        Assert.Contains("public const string SetId = \"sample-set\";", source, StringComparison.Ordinal);
        Assert.DoesNotContain("urn:ledger-set", source, StringComparison.Ordinal);
    }

    [Fact]
    public void An_accepted_decision_in_the_export_is_not_obsolete()
    {
        GeneratorHarness.Result result = Run(Citation("ExportedKey"), Fixture());

        Assert.Empty(result.GeneratorDiagnostics);
        Assert.Empty(result.CompilationDiagnostics.Where(d => d.Id is "CS0618" or "CS0619" || d.Severity == DiagnosticSeverity.Error));
    }

    [Fact]
    public void An_unaccepted_decision_in_the_export_is_obsolete_as_a_warning()
    {
        GeneratorHarness.Result result = Run(Citation("UnacceptedKey"), Fixture());

        Diagnostic obsolete = Assert.Single(result.CompilationDiagnostics.Where(d => d.Id == "CS0618"));
        Assert.Equal(DiagnosticSeverity.Warning, obsolete.Severity);
    }

    [Fact]
    public void A_node_is_read_whatever_order_its_types_arrive_in()
    {
        // The export types every node twice, ledger:X and prov:Entity, and in code-point order the
        // prov: line comes first. Reversed, the ledger: line comes first and prov:Entity last; a
        // reader that kept only the last type read would drop every decision, version and acceptance.
        string sorted = Fixture();
        string reversed = string.Join("\n", sorted.Split('\n').Reverse());

        GeneratorHarness.Result fromSorted = Run(Citation("ExportedKey"), sorted);
        GeneratorHarness.Result fromReversed = Run(Citation("ExportedKey"), reversed);

        string? a = fromSorted.GeneratedSource("DecisionDriven.Ledger.SampleNs.g.cs");
        string? b = fromReversed.GeneratedSource("DecisionDriven.Ledger.SampleNs.g.cs");

        Assert.NotNull(a);
        Assert.Equal(a, b);
        Assert.Empty(fromReversed.CompilationDiagnostics.Where(d => d.Id is "CS0618" or "CS0619" || d.Severity == DiagnosticSeverity.Error));
    }

    [Fact]
    public void An_acceptance_named_by_a_revocation_node_stops_counting()
    {
        // DecisionsAsTypes.RevocationIsItsOwnNode. RevokedKey's only acceptance is revoked by a
        // ledger:Revocation node, which leaves the tip unaccepted: a warning, not an error.
        GeneratorHarness.Result result = Run(Citation("RevokedKey"), Fixture());

        Diagnostic obsolete = Assert.Single(result.CompilationDiagnostics.Where(d => d.Id == "CS0618"));
        Assert.Equal(DiagnosticSeverity.Warning, obsolete.Severity);
        Assert.Empty(result.GeneratorDiagnostics);
    }

    [Fact]
    public void An_acceptance_revoked_on_its_own_node_still_stops_counting_and_is_warned_about()
    {
        // The transition shape: ledger:revokedAt on the acceptance. Still a revocation, so an export
        // written before the change keeps its meaning, and DDGEN0006 says it is the old shape.
        string export = LedgerInput.NTriples(
            "sample-set", "SomeKey", "A statement.", acceptedBy: "mailto:someone@example.com", acceptanceRevokedAt: "2026-10-02T00:00:00Z");

        GeneratorHarness.Result result = GeneratorHarness.Run(
            "internal sealed class Citing { private static readonly global::System.Type D = typeof(global::DecisionDriven.Ledger.SampleNs.SampleSet.SomeKey); }",
            new[] { LedgerInput.AsExport(export) });

        Assert.Single(result.CompilationDiagnostics.Where(d => d.Id == "CS0618"));
        Diagnostic warning = Assert.Single(result.GeneratorDiagnostics);
        Assert.Equal("DDGEN0006", warning.Id);
        Assert.Equal(DiagnosticSeverity.Warning, warning.Severity);
    }

    [Fact]
    public void An_acceptance_revoked_in_both_shapes_is_revoked_once_and_warned_about_once()
    {
        const string Acceptance = "urn:acc:01JA0000000000000000000031";
        string export = Fixture() + "\n<" + Acceptance + "> <urn:ledger:ns#revokedAt> \"2026-10-02T10:00:00Z\" .\n";

        GeneratorHarness.Result result = Run(Citation("RevokedKey"), export);

        Assert.Single(result.CompilationDiagnostics.Where(d => d.Id == "CS0618"));
        Diagnostic warning = Assert.Single(result.GeneratorDiagnostics);
        Assert.Equal("DDGEN0006", warning.Id);
        Assert.Contains("'" + Acceptance + "'", warning.GetMessage(), StringComparison.Ordinal);
    }

    [Fact]
    public void A_revocation_of_something_other_than_an_acceptance_revokes_no_acceptance()
    {
        // The same shape revokes an authority grant. Nothing the generator reads is a grant, so the
        // revocation has nothing to act on and the accepted decision stays accepted.
        string export = Fixture()
            + "\n<urn:rev:grant> <http://www.w3.org/1999/02/22-rdf-syntax-ns#type> <urn:ledger:ns#Revocation> ."
            + "\n<urn:rev:grant> <urn:ledger:ns#revokes> <urn:grant:01JA0000000000000000000099> .\n";

        GeneratorHarness.Result result = Run(Citation("ExportedKey"), export);

        Assert.Empty(result.GeneratorDiagnostics);
        Assert.Empty(result.CompilationDiagnostics.Where(d => d.Id is "CS0618" or "CS0619"));
    }

    [Fact]
    public void The_on_node_revocation_message_is_exactly_this()
    {
        // DiagnosticMessages.ExactMessageTested.
        string export = LedgerInput.NTriples(
            "sample-set", "SomeKey", "A statement.", acceptedBy: "mailto:someone@example.com", acceptanceRevokedAt: "2026-10-02T00:00:00Z");

        Diagnostic warning = Assert.Single(GeneratorHarness.Run(string.Empty, new[] { LedgerInput.AsExport(export) }).GeneratorDiagnostics);

        Assert.Equal(
            "export.nt: acceptance 'urn:acceptance:SomeKey' is revoked with ledger:revokedAt on the acceptance node, "
            + "the shape the ledger export no longer writes; it is read as revoked. "
            + "Decide: re-export the ledger, which writes a ledger:Revocation node naming the acceptance with ledger:revokes, "
            + "or keep this export until a re-export is possible. "
            + "The on-node shape is read only during the transition, and an export that still uses it will stop revoking anything when that ends.",
            warning.GetMessage());
    }

    internal static string Fixture() =>
        File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Ledger", "Fixtures", "export-v2.nt"));

    internal static GeneratorHarness.Result Run(string source, string export) =>
        GeneratorHarness.Run(source, new[] { LedgerInput.AsExport(export) });

    internal static string Citation(string key) =>
        "internal sealed class Citing { private static readonly global::System.Type D = typeof(global::DecisionDriven.Ledger.SampleNs.SampleSet." + key + "); }";
}
