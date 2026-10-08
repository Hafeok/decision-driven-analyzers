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

    internal static string Fixture() =>
        File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Ledger", "Fixtures", "export-v2.nt"));

    internal static GeneratorHarness.Result Run(string source, string export) =>
        GeneratorHarness.Run(source, new[] { LedgerInput.AsExport(export) });

    internal static string Citation(string key) =>
        "internal sealed class Citing { private static readonly global::System.Type D = typeof(global::DecisionDriven.Ledger.SampleNs.SampleSet." + key + "); }";
}
