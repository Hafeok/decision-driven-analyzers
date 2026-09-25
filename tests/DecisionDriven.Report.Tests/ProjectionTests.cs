using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using DecisionDriven.Report.Citations;
using DecisionDriven.Report.Metadata;
using DecisionDriven.Report.Output;
using DecisionDriven.Report.Tests.Fixtures;
using Xunit;

namespace DecisionDriven.Report.Tests;

/// <summary>
/// The citation projection, over a real repository whose history the tests write.
/// </summary>
/// <remarks>
/// The history: the ledger is filed; then the code citing it is committed; then a decision's
/// statement changes. So the citation was written against a version that is no longer the tip,
/// which is the case the projection exists to surface - and the one no amount of reading the
/// current tree could find.
/// </remarks>
public sealed class ProjectionTests : IDisposable
{
    private const string Ledger = "docs/decisions";

    private readonly Scratch repo = new Scratch();
    private readonly string ledgerCommit;
    private readonly string citingCommit;
    private readonly string revisedCommit;
    private readonly string assembly;

    public ProjectionTests()
    {
        repo.Init();

        repo.Write("docs/decisions/shape.md", SetFile("The store's read side is one contract"));
        ledgerCommit = repo.Commit("File the decisions");

        repo.Write("src/Contracts.cs", Citing);
        repo.Write("src/Pool.cs", Pooled);
        repo.Write("src/Markers.cs", Build.Markers);
        repo.Write("src/Decisions.cs", Decisions);
        citingCommit = repo.Commit("Cite them");

        repo.Write("docs/decisions/shape.md", SetFile("The store's read side is two contracts"));
        revisedCommit = repo.Commit("Revise the contract decision");

        assembly = Build.Assembly(
            repo.PathOf("bin"),
            "Fix.Cites",
            new[] { "src/Contracts.cs", "src/Pool.cs", "src/Markers.cs", "src/Decisions.cs" }
                .ToDictionary(file => repo.PathOf(file), file => File.ReadAllText(repo.PathOf(file))));
    }

    [Fact]
    public void Every_citing_symbol_is_found_with_the_decision_its_type_names()
    {
        List<Citation> found = Find();

        Assert.Equal(
            new[]
            {
                ("M:Fix.Cites.Pool.Rent", "DesignDecision", "dec:fixture/PoolIsIntended", "Pool"),
                ("N:Fix.Cites.Model", "DomainModel", "dec:fixture/ModelNamespace", (string?)null),
                ("T:Fix.Cites.IQuadSource", "Contract", "dec:fixture/QuadSourceContract", (string?)null),
            },
            found
                .OrderBy(c => c.Symbol, StringComparer.Ordinal)
                .Select(c => (c.Symbol, c.Attribute, c.DecisionId, c.Scope)));
    }

    [Fact]
    public void An_interface_is_traced_to_its_file_although_it_has_no_method_bodies()
    {
        // No sequence points: the file comes from the TypeDefinitionDocuments record the compiler
        // writes for exactly this case. Without it, most contracts would have no file.
        Citation contract = Assert.Single(Find(), c => c.Attribute == "Contract");

        Assert.Equal("src/Contracts.cs", Sources.InRepository(contract.SourceFile!, repo.Root));
    }

    [Fact]
    public void A_citation_is_dated_by_the_commit_that_introduced_it_and_the_version_it_cited()
    {
        Attributed contract = Assert.Single(Project().Citations, c => c.Citation.Attribute == "Contract");

        Assert.Equal(citingCommit, contract.Commit);
        Assert.Equal("src/Contracts.cs", contract.SourceFile);

        // Cited while the statement said "one contract"; the tip now says "two".
        Assert.NotNull(contract.CitesVersion);
        Assert.NotNull(contract.CurrentVersion);
        Assert.NotEqual(contract.CitesVersion, contract.CurrentVersion);
        Assert.True(contract.Stale);
    }

    [Fact]
    public void A_citation_of_an_unchanged_decision_is_not_stale()
    {
        Attributed pool = Assert.Single(Project().Citations, c => c.Citation.Attribute == "DesignDecision");

        Assert.Equal(citingCommit, pool.Commit);
        Assert.False(pool.Stale);
    }

    [Fact]
    public void A_decision_nothing_cites_is_listed_as_uncited()
    {
        Assert.Equal(new[] { "dec:fixture/NobodyCitesThis" }, Project().Uncited.Select(d => d.Id));
    }

    [Fact]
    public void Newly_cited_is_relative_to_the_since_ref()
    {
        Assert.All(Project(since: ledgerCommit).Citations, c => Assert.True(c.NewSince));
        Assert.All(Project(since: citingCommit).Citations, c => Assert.False(c.NewSince));
        Assert.All(Project(since: revisedCommit).Citations, c => Assert.False(c.NewSince));
    }

    [Fact]
    public void The_triples_carry_every_predicate_the_decision_names()
    {
        Projection projection = Project();
        string triples = CitationTriples.Write(projection, "sha1");

        Attributed contract = Assert.Single(projection.Citations, c => c.Citation.Attribute == "Contract");
        string subject = "<" + CitationTriples.CitationIri(contract.Citation) + ">";

        string[] lines = triples.Split('\n', StringSplitOptions.RemoveEmptyEntries);

        Assert.Contains(subject + " <http://www.w3.org/1999/02/22-rdf-syntax-ns#type> <urn:ledger:ns#Citation> .", lines);
        Assert.Contains(subject + " <http://www.w3.org/1999/02/22-rdf-syntax-ns#type> <http://www.w3.org/ns/prov#Entity> .", lines);
        Assert.Contains(subject + " <urn:ledger:ns#ofDecision> <dec:fixture/QuadSourceContract> .", lines);
        Assert.Contains(subject + " <urn:ledger:ns#symbol> \"T:Fix.Cites.IQuadSource\" .", lines);
        Assert.Contains(subject + " <urn:ledger:ns#attribute> \"Contract\" .", lines);
        Assert.Contains(subject + " <urn:ledger:ns#citesVersion> <" + contract.CitesVersion + "> .", lines);
        Assert.Contains(subject + " <http://www.w3.org/ns/prov#wasGeneratedBy> <urn:git:sha1:" + citingCommit + "> .", lines);
        Assert.Contains("<urn:git:sha1:" + citingCommit + "> <http://www.w3.org/1999/02/22-rdf-syntax-ns#type> <urn:ledger:ns#Commit> .", lines);
        Assert.Contains("<urn:git:sha1:" + citingCommit + "> <http://www.w3.org/1999/02/22-rdf-syntax-ns#type> <http://www.w3.org/ns/prov#Activity> .", lines);

        Attributed pool = Assert.Single(projection.Citations, c => c.Citation.Attribute == "DesignDecision");
        Assert.Contains("<" + CitationTriples.CitationIri(pool.Citation) + "> <urn:ledger:ns#exceptionScope> \"Pool\" .", lines);

        // Sorted and deterministic, so a diff of two runs is a diff of what changed.
        Assert.Equal(lines.OrderBy(l => l, StringComparer.Ordinal), lines);
        Assert.Equal(triples, CitationTriples.Write(Project(), "sha1"));
    }

    [Fact]
    public void The_command_line_writes_both_outputs_and_exits_zero()
    {
        string output = repo.PathOf("out");
        StringWriter stdout = new StringWriter();
        StringWriter stderr = new StringWriter();

        (Options? options, string? error) = Options.Parse(new[]
        {
            "--assembly", repo.PathOf("bin"),
            "--repo", repo.Root,
            "--ledger", Ledger,
            "--since", ledgerCommit,
            "--out", output,
        });

        Assert.Null(error);
        Assert.Equal(0, Program.Run(options!, stdout, stderr));

        string markdown = File.ReadAllText(Path.Combine(output, "report.md"));
        Assert.Contains("## Citations", markdown, StringComparison.Ordinal);
        Assert.Contains("`dec:fixture/NobodyCitesThis`", markdown, StringComparison.Ordinal);
        Assert.Contains("`T:Fix.Cites.IQuadSource` [Contract] cites `dec:fixture/QuadSourceContract` in `src/Contracts.cs`", markdown, StringComparison.Ordinal);
        Assert.Contains("### Newly cited since", markdown, StringComparison.Ordinal);

        Assert.True(File.Exists(Path.Combine(output, "citations.nt")));
        Assert.Equal(markdown, stdout.ToString());
    }

    [Theory]
    [InlineData(new string[0], true, null)]
    [InlineData(new[] { "--version" }, true, null)]
    [InlineData(new[] { "--repo", "." }, false, "Nothing to read: give at least one --assembly.")]
    [InlineData(new[] { "--assembly" }, false, "--assembly needs a value.")]
    [InlineData(new[] { "--nonsense", "x" }, false, "Unknown option --nonsense.")]
    public void The_command_line_is_parsed_or_refused_with_a_reason(string[] args, bool parses, string? error)
    {
        (Options? options, string? reason) = Options.Parse(args);

        Assert.Equal(parses, options is not null);
        Assert.Equal(error, reason);
    }

    public void Dispose() => repo.Dispose();

    private List<Citation> Find()
    {
        using LoadedAssembly loaded = LoadedAssembly.Open(assembly)!;
        return CitationFinder.Find(new[] { loaded });
    }

    private Projection Project(string? since = null)
    {
        Git git = new Git(repo.Root);
        return Projection.Build(Find(), git, new LedgerHistory(git, repo.Root, Ledger), repo.Root, Ledger, since);
    }

    private static string SetFile(string contractStatement) =>
        "---\n"
        + "set: shape\n"
        + "namespace: fixture\n"
        + "decisions:\n"
        + "  - key: QuadSourceContract\n"
        + "    statement: \"" + contractStatement + "\"\n"
        + "  - key: PoolIsIntended\n"
        + "    statement: \"This pool is intended\"\n"
        + "  - key: ModelNamespace\n"
        + "    statement: \"Fix.Cites.Model is the model\"\n"
        + "  - key: NobodyCitesThis\n"
        + "    statement: \"Filed and never cited\"\n"
        + "---\n";

    private const string Citing =
        "[assembly: DecisionDriven.DomainModel(\"Fix.Cites.Model\", typeof(DecisionDriven.Ledger.Fixture.Shape.ModelNamespace))]\n"
        + "namespace Fix.Cites\n"
        + "{\n"
        + "    [DecisionDriven.Contract(typeof(DecisionDriven.Ledger.Fixture.Shape.QuadSourceContract), Role = \"store read side\")]\n"
        + "    public interface IQuadSource\n"
        + "    {\n"
        + "        int Read();\n"
        + "    }\n"
        + "}\n"
        + "namespace Fix.Cites.Model { public readonly record struct Quad(int Value); }\n";

    private const string Pooled =
        "namespace Fix.Cites\n"
        + "{\n"
        + "    public static class Pool\n"
        + "    {\n"
        + "        [DecisionDriven.DesignDecision(typeof(DecisionDriven.Ledger.Fixture.Shape.PoolIsIntended), Scope = DecisionDriven.ExceptionScope.Pool)]\n"
        + "        public static byte[] Rent() => new byte[16];\n"
        + "    }\n"
        + "}\n";

    private const string Decisions =
        "namespace DecisionDriven.Ledger.Fixture\n"
        + "{\n"
        + "    internal static class Shape\n"
        + "    {\n"
        + "        public const string SetId = \"shape\";\n"
        + "        public static class QuadSourceContract { public const string Id = \"dec:fixture/QuadSourceContract\"; public const string Key = \"QuadSourceContract\"; public const string Namespace = \"fixture\"; }\n"
        + "        public static class PoolIsIntended { public const string Id = \"dec:fixture/PoolIsIntended\"; public const string Key = \"PoolIsIntended\"; public const string Namespace = \"fixture\"; }\n"
        + "        public static class ModelNamespace { public const string Id = \"dec:fixture/ModelNamespace\"; public const string Key = \"ModelNamespace\"; public const string Namespace = \"fixture\"; }\n"
        + "        public static class NobodyCitesThis { public const string Id = \"dec:fixture/NobodyCitesThis\"; public const string Key = \"NobodyCitesThis\"; public const string Namespace = \"fixture\"; }\n"
        + "    }\n"
        + "}\n";
}
