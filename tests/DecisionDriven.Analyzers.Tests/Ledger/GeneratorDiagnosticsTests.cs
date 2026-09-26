using System;
using System.Linq;
using Microsoft.CodeAnalysis;
using Xunit;

namespace DecisionDriven.Analyzers.Tests.Ledger;

/// <summary>
/// What the generator says when the ledger it was handed does not make sense.
/// </summary>
/// <remarks>
/// All four are errors. ADR-A07 wants a malformed export to break every project rather than to
/// quietly stop emitting decision types, because the second failure shows up as "type not found" at
/// every citation and points at none of the files that are actually wrong.
/// </remarks>
public sealed class GeneratorDiagnosticsTests
{
    private const string Empty = "internal sealed class Nothing { }";
    private const string Ledger = "urn:ledger:ns#";
    private const string Rdf = "http://www.w3.org/1999/02/22-rdf-syntax-ns#";

    [Fact]
    public void Two_decisions_claiming_one_key_is_an_error()
    {
        string first = LedgerInput.FrontMatter("set-one", "SameKey", "The first.");
        string second = LedgerInput.FrontMatter("set-two", "SameKey", "The second.");

        GeneratorHarness.Result result = GeneratorHarness.Run(
            Empty,
            new[]
            {
                LedgerInput.AsSet(first, "decisions/one.md"),
                LedgerInput.AsSet(second, "decisions/two.md"),
            });

        Diagnostic diagnostic = Assert.Single(result.GeneratorDiagnostics.Where(d => d.Id == "DDGEN0001"));
        Assert.Equal(DiagnosticSeverity.Error, diagnostic.Severity);

        string message = diagnostic.GetMessage();
        Assert.Contains("SameKey", message, StringComparison.Ordinal);
        Assert.Contains(LedgerInput.Namespace, message, StringComparison.Ordinal);
    }

    [Fact]
    public void The_same_key_in_two_namespaces_is_fine()
    {
        // Uniqueness is per namespace, which is the whole reason the generated namespace is
        // prefixed by the ledger namespace.
        string first = LedgerInput.FrontMatter("set-one", "SameKey", "The first.");
        string second = first.Replace("namespace: " + LedgerInput.Namespace, "namespace: other-ns", StringComparison.Ordinal);

        GeneratorHarness.Result result = GeneratorHarness.Run(
            Empty,
            new[]
            {
                LedgerInput.AsSet(first, "decisions/one.md"),
                LedgerInput.AsSet(second, "decisions/two.md"),
            });

        Assert.Empty(result.GeneratorDiagnostics.Where(d => d.Id == "DDGEN0001"));
    }

    [Theory]
    [InlineData("lowerFirst")]
    [InlineData("Has-A-Dash")]
    [InlineData("Has Space")]
    [InlineData("9LeadingDigit")]
    public void A_key_that_is_not_an_identifier_is_an_error(string key)
    {
        GeneratorHarness.Result result = GeneratorHarness.Run(
            Empty,
            new[] { LedgerInput.AsSet(LedgerInput.FrontMatter("sample-set", key, "A statement.")) });

        Diagnostic diagnostic = Assert.Single(result.GeneratorDiagnostics.Where(d => d.Id == "DDGEN0002"));
        Assert.Equal(DiagnosticSeverity.Error, diagnostic.Severity);
        Assert.Contains(key, diagnostic.GetMessage(), StringComparison.Ordinal);
    }

    [Fact]
    public void A_key_of_sixty_five_characters_is_an_error()
    {
        // The syntax is ^[A-Z][A-Za-z0-9]{0,63}$, so 64 is the longest that is legal.
        string legal = "A" + new string('b', 63);
        string tooLong = "A" + new string('b', 64);

        Assert.Empty(Run(legal).GeneratorDiagnostics.Where(d => d.Id == "DDGEN0002"));
        Assert.Single(Run(tooLong).GeneratorDiagnostics.Where(d => d.Id == "DDGEN0002"));

        static GeneratorHarness.Result Run(string key) => GeneratorHarness.Run(
            Empty,
            new[] { LedgerInput.AsSet(LedgerInput.FrontMatter("sample-set", key, "A statement.")) });
    }

    [Fact]
    public void A_version_whose_key_differs_from_the_version_it_revises_is_an_error()
    {
        // DecisionsAsTypes.VersionLevelKeyCarriedAcrossSupersession: the key is immutable across
        // versions. Changing one silently breaks every citation written against the old key, and
        // nothing else in the build would notice.
        string export =
            "<dec:sample-ns/One> <" + Rdf + "type> <" + Ledger + "Decision> .\n"
            + "<dec:sample-ns/One> <" + Ledger + "namespace> \"sample-ns\" .\n"
            + "<urn:v1> <" + Rdf + "type> <" + Ledger + "DecisionVersion> .\n"
            + "<urn:v1> <" + Ledger + "ofDecision> <dec:sample-ns/One> .\n"
            + "<urn:v1> <" + Ledger + "set> \"sample-set\" .\n"
            + "<urn:v1> <" + Ledger + "key> \"OriginalKey\" .\n"
            + "<urn:v2> <" + Rdf + "type> <" + Ledger + "DecisionVersion> .\n"
            + "<urn:v2> <" + Ledger + "ofDecision> <dec:sample-ns/One> .\n"
            + "<urn:v2> <" + Ledger + "set> \"sample-set\" .\n"
            + "<urn:v2> <" + Ledger + "key> \"RenamedKey\" .\n"
            + "<urn:v2> <http://www.w3.org/ns/prov#wasRevisionOf> <urn:v1> .\n";

        GeneratorHarness.Result result = GeneratorHarness.Run(Empty, new[] { LedgerInput.AsExport(export) });

        Diagnostic diagnostic = Assert.Single(result.GeneratorDiagnostics.Where(d => d.Id == "DDGEN0003"));
        Assert.Equal(DiagnosticSeverity.Error, diagnostic.Severity);

        string message = diagnostic.GetMessage();
        Assert.Contains("RenamedKey", message, StringComparison.Ordinal);
        Assert.Contains("OriginalKey", message, StringComparison.Ordinal);
    }

    [Fact]
    public void A_line_that_is_not_a_triple_is_an_error_naming_its_line_number()
    {
        // The line number is the point. An export is generated, so the author's next move is to
        // look at the line; a diagnostic that only said "the export is malformed" would not help.
        string export = LedgerInput.NTriples("sample-set", "SomeKey", "A statement.")
            + "this is not a triple\n";

        GeneratorHarness.Result result = GeneratorHarness.Run(Empty, new[] { LedgerInput.AsExport(export) });

        Diagnostic diagnostic = Assert.Single(result.GeneratorDiagnostics.Where(d => d.Id == "DDGEN0004"));
        Assert.Equal(DiagnosticSeverity.Error, diagnostic.Severity);

        string message = diagnostic.GetMessage();
        Assert.Contains("export.nt", message, StringComparison.Ordinal);
        Assert.Contains("(8)", message, StringComparison.Ordinal);
    }

    [Fact]
    public void Blank_lines_and_comments_are_not_errors()
    {
        string export = "# a comment\n\n"
            + LedgerInput.NTriples("sample-set", "SomeKey", "A statement.")
            + "\n   \n";

        GeneratorHarness.Result result = GeneratorHarness.Run(Empty, new[] { LedgerInput.AsExport(export) });

        Assert.Empty(result.GeneratorDiagnostics.Where(d => d.Id == "DDGEN0004"));
    }

    [Fact]
    public void A_file_without_the_DdLedger_metadata_is_not_read_at_all()
    {
        // The metadata is what tells a decision set apart from whatever else a consumer has put in
        // AdditionalFiles. Reading every AdditionalFile would make an unrelated markdown file with
        // a "set:" line into a decision set.
        GeneratorHarness.Result result = GeneratorHarness.Run(
            Empty,
            new[] { new GeneratorHarness.LedgerFile("decisions/set.md", string.Empty, LedgerInput.FrontMatter("sample-set", "SomeKey", "A statement.")) });

        Assert.Null(result.GeneratedSource("DecisionDriven.Ledger.SampleNs.g.cs"));
    }

    [Theory]
    [InlineData("catalog-is-read-only", "CatalogIsReadOnly", "CatalogIsReadOnly")]
    [InlineData("catalog-is-read-only", "SetId", "SetId")]
    public void A_key_that_collides_with_a_generated_member_is_an_error_and_not_emitted(string setId, string key, string member)
    {
        // The shape a real ledger met: a set named for its ADR's title and a decision named for
        // the same sentence. Before DDGEN0005 this compiled to a nested class named like its
        // enclosing class, and every consuming project failed with CS0542 inside generated code.
        string text = TwoDecisions(setId, key, "KeepsTheRestCompiling");

        GeneratorHarness.Result result = GeneratorHarness.Run(Empty, new[] { LedgerInput.AsSet(text) });

        Diagnostic diagnostic = Assert.Single(result.GeneratorDiagnostics.Where(d => d.Id == "DDGEN0005"));
        Assert.Equal(DiagnosticSeverity.Error, diagnostic.Severity);
        Assert.Contains(member, diagnostic.GetMessage(), StringComparison.Ordinal);

        // The colliding decision is left out and the rest of the set is emitted and compiles.
        Assert.Empty(result.CompilationDiagnostics.Where(d => d.Severity == DiagnosticSeverity.Error));
        string source = Assert.IsType<string>(result.GeneratedSource("DecisionDriven.Ledger.SampleNs.g.cs"));
        Assert.Contains("public static class KeepsTheRestCompiling", source, StringComparison.Ordinal);
        Assert.DoesNotContain("public static class " + key + "\n", source, StringComparison.Ordinal);
    }

    [Fact]
    public void A_key_that_merely_starts_like_its_set_is_fine()
    {
        // Only exact equality collides. A key sharing its set's words is the ordinary case.
        string text = TwoDecisions("catalog-is-read-only", "CatalogIsReadOnlyForever", "SetIdentity");

        GeneratorHarness.Result result = GeneratorHarness.Run(Empty, new[] { LedgerInput.AsSet(text) });

        Assert.Empty(result.GeneratorDiagnostics.Where(d => d.Id == "DDGEN0005"));
        Assert.Empty(result.CompilationDiagnostics.Where(d => d.Severity == DiagnosticSeverity.Error));
    }

    [Fact]
    public void The_key_collision_message_is_exact()
    {
        string text = TwoDecisions("catalog-is-read-only", "CatalogIsReadOnly", "KeepsTheRestCompiling");

        GeneratorHarness.Result result = GeneratorHarness.Run(Empty, new[] { LedgerInput.AsSet(text) });

        Diagnostic diagnostic = Assert.Single(result.GeneratorDiagnostics.Where(d => d.Id == "DDGEN0005"));
        Assert.Equal(
            "Decision key 'CatalogIsReadOnly' in set 'catalog-is-read-only' of ledger namespace 'sample-ns' collides with 'CatalogIsReadOnly', which the generator emits for the set, so the decision cannot be emitted. "
                + "Decide: rename the key, or move the decision to a set whose class name it does not repeat. "
                + "A key becomes a type nested in its set's class, and C# allows neither a member named like its enclosing type nor two members of one name.",
            diagnostic.GetMessage());
    }

    private static string TwoDecisions(string setId, string firstKey, string secondKey) =>
        "---" + Environment.NewLine
        + "set: " + setId + Environment.NewLine
        + "namespace: " + LedgerInput.Namespace + Environment.NewLine
        + "decisions:" + Environment.NewLine
        + "  - key: " + firstKey + Environment.NewLine
        + "    statement: \"The first.\"" + Environment.NewLine
        + "  - key: " + secondKey + Environment.NewLine
        + "    statement: \"The second.\"" + Environment.NewLine
        + "---" + Environment.NewLine;
}
