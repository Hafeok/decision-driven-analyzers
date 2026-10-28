using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Threading.Tasks;
using DecisionDriven.Analyzers.Rules;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Testing;
using Xunit;

namespace DecisionDriven.Analyzers.Tests.Rules;

/// <summary>
/// DD0022: a namespace declaration names the file's folder.
/// </summary>
public sealed class NamespaceFolderTests
{
    private const string ProjectDir = "/proj/";

    /// <summary>
    /// <c>DiagnosticProperties.NamespaceFolderNewName</c>, by value: the file is linked into both
    /// the analyzer and the code-fixes assembly, so naming the type here would be ambiguous.
    /// </summary>
    private const string NewName = "NewName";

    [Theory]
    [InlineData("namespace Sample.Layer1.Ingest { public sealed class Reader { } }")]
    [InlineData("namespace Sample.Layer1.Ingest; public sealed class Reader { }")]
    public void A_declaration_that_names_the_folder_is_not_reported(string source)
    {
        Assert.Empty(Run("Ingest/Reader.cs", source));
    }

    [Theory]
    [InlineData("namespace Sample.Layer1 { public sealed class Reader { } }", "Sample.Layer1")]
    [InlineData("namespace Sample.Layer1; public sealed class Reader { }", "Sample.Layer1")]
    [InlineData("namespace Sample.Layer1.Parsing { public sealed class Reader { } }", "Sample.Layer1.Parsing")]
    public void A_declaration_that_does_not_name_the_folder_is_reported_at_the_name(string source, string actual)
    {
        Diagnostic diagnostic = Assert.Single(Run("Ingest/Reader.cs", source));

        Assert.Equal("DD0022", diagnostic.Id);
        Assert.Equal(DiagnosticSeverity.Error, diagnostic.Severity);
        Assert.Contains($"namespace '{actual}' is declared in 'Ingest/Reader.cs', whose folder names 'Sample.Layer1.Ingest'", diagnostic.GetMessage(), StringComparison.Ordinal);
        Assert.Equal(actual, diagnostic.Location.SourceTree!.GetText(TestContext.Current.CancellationToken).ToString(diagnostic.Location.SourceSpan));
        Assert.Equal("Sample.Layer1.Ingest", diagnostic.Properties[NewName]);
    }

    [Fact]
    public void A_file_at_the_project_root_expects_the_root_namespace()
    {
        Assert.Empty(Run("Reader.cs", "namespace Sample.Layer1 { public sealed class Reader { } }"));
        Assert.Single(Run("Reader.cs", "namespace Sample.Layer1.Ingest { public sealed class Reader { } }"));
    }

    [Fact]
    public void Nested_folders_are_one_segment_each()
    {
        Assert.Empty(Run("Ingest/Readers/Csv.cs", "namespace Sample.Layer1.Ingest.Readers { public sealed class Csv { } }"));

        Diagnostic diagnostic = Assert.Single(Run("Ingest/Readers/Csv.cs", "namespace Sample.Layer1.Ingest { public sealed class Csv { } }"));
        Assert.Contains("whose folder names 'Sample.Layer1.Ingest.Readers'", diagnostic.GetMessage(), StringComparison.Ordinal);
    }

    [Fact]
    public void RootNamespace_replaces_the_assembly_name_as_the_root()
    {
        Dictionary<string, string> properties = Properties();
        properties["build_property.RootNamespace"] = "Sample.Root";

        Assert.Empty(RuleHarness.RunOn(new NamespaceFolderAnalyzer(), Compile("Ingest/Reader.cs", "namespace Sample.Root.Ingest { public sealed class Reader { } }"), properties));

        Diagnostic diagnostic = Assert.Single(RuleHarness.RunOn(new NamespaceFolderAnalyzer(), Compile("Ingest/Reader.cs", "namespace Sample.Layer1.Ingest { public sealed class Reader { } }"), properties));
        Assert.Contains("whose folder names 'Sample.Root.Ingest'", diagnostic.GetMessage(), StringComparison.Ordinal);
    }

    [Fact]
    public void An_empty_RootNamespace_is_unset_and_the_assembly_name_is_the_root()
    {
        Dictionary<string, string> properties = Properties();
        properties["build_property.RootNamespace"] = string.Empty;

        Assert.Empty(RuleHarness.RunOn(new NamespaceFolderAnalyzer(), Compile("Ingest/Reader.cs", "namespace Sample.Layer1.Ingest { public sealed class Reader { } }"), properties));
    }

    [Theory]
    [InlineData("public sealed class Reader { }", "Reader")]
    [InlineData("public delegate void Read();", "Read")]
    [InlineData("public enum Mode { A }", "Mode")]
    public void A_type_in_the_global_namespace_is_reported_at_its_identifier(string source, string identifier)
    {
        Diagnostic diagnostic = Assert.Single(Run("Ingest/Reader.cs", source));

        Assert.Contains($"type '{identifier}' in 'Ingest/Reader.cs' is in the global namespace, and the file's folder names 'Sample.Layer1.Ingest'", diagnostic.GetMessage(), StringComparison.Ordinal);
        Assert.Contains("declare it in 'Sample.Layer1.Ingest'", diagnostic.GetMessage(), StringComparison.Ordinal);
        Assert.Equal(identifier, diagnostic.Location.SourceTree!.GetText(TestContext.Current.CancellationToken).ToString(diagnostic.Location.SourceSpan));
        Assert.False(diagnostic.Properties.ContainsKey(NewName));
    }

    [Fact]
    public void A_folder_that_is_not_an_identifier_is_the_finding_once_per_file()
    {
        // Two declarations, neither of which could match: one finding, at the first.
        ImmutableArray<Diagnostic> diagnostics = Run(
            "Ingest/my-stuff/Reader.cs",
            "namespace Sample.Layer1.Ingest { public sealed class Reader { } } namespace Other { public sealed class Writer { } }");

        Diagnostic diagnostic = Assert.Single(diagnostics);
        Assert.Contains("'Ingest/my-stuff/Reader.cs' lies in folder 'my-stuff', which is not a valid C# identifier, so no namespace can name it", diagnostic.GetMessage(), StringComparison.Ordinal);
        Assert.Contains("rename the folder to an identifier, or move the file out of it", diagnostic.GetMessage(), StringComparison.Ordinal);
        Assert.Equal("Sample.Layer1.Ingest", diagnostic.Location.SourceTree!.GetText(TestContext.Current.CancellationToken).ToString(diagnostic.Location.SourceSpan));

        // At the type when there is no namespace declaration to point at.
        Diagnostic global = Assert.Single(Run("Ingest/2024/Reader.cs", "public sealed class Reader { }"));
        Assert.Equal("Reader", global.Location.SourceTree!.GetText(TestContext.Current.CancellationToken).ToString(global.Location.SourceSpan));

        // A file that declares nothing has nothing to report on.
        Assert.Empty(Run("Ingest/my-stuff/Empty.cs", "// nothing here"));
    }

    [Fact]
    public void A_file_outside_the_project_directory_is_not_checked()
    {
        // A linked file belongs to another project's structure.
        Assert.Empty(Run("/elsewhere/Shared/Reader.cs", "namespace Sample.Layer1 { public sealed class Reader { } }"));

        // As the compiler hands a linked file over: the project directory followed by the relative
        // path it was linked by, '..' included. Collapsed, it is the file above, not a file in a
        // folder named '..'.
        Assert.Empty(Run("/proj/../elsewhere/Shared/Reader.cs", "namespace Sample.Layer1 { public sealed class Reader { } }"));
    }

    [Fact]
    public void Dot_segments_in_a_path_under_the_project_are_collapsed_before_comparing()
    {
        Assert.Empty(Run("/proj/./Ingest/../Ingest/Reader.cs", "namespace Sample.Layer1.Ingest { public sealed class Reader { } }"));

        Dictionary<string, string> properties = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["build_property.ProjectDir"] = "/src/Other/../proj/",
        };

        Assert.Empty(RuleHarness.RunOn(new NamespaceFolderAnalyzer(), Compile("/src/proj/Ingest/Reader.cs", "namespace Sample.Layer1.Ingest { public sealed class Reader { } }"), properties));
    }

    [Theory]
    [InlineData("Ingest/Reader.g.cs")]
    [InlineData("obj/Release/net10.0/Reader.cs")]
    public void Generated_code_is_not_checked(string path)
    {
        // RuleTiers.GeneratedCodeIsExempt: a generated declaration is the generator author's.
        Dictionary<string, string> properties = Properties();
        properties["build_property.IntermediateOutputPath"] = "obj/Release/net10.0/";

        Assert.Empty(RuleHarness.RunOn(new NamespaceFolderAnalyzer(), Compile(path, "namespace Other { public sealed class Reader { } }"), properties));
    }

    [Fact]
    public void Windows_paths_are_compared_by_segment()
    {
        Dictionary<string, string> properties = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["build_property.ProjectDir"] = @"C:\src\Sample.Layer1\",
        };

        Assert.Empty(RuleHarness.RunOn(
            new NamespaceFolderAnalyzer(),
            Compile(@"C:\src\Sample.Layer1\Ingest\Reader.cs", "namespace Sample.Layer1.Ingest { public sealed class Reader { } }"),
            properties));

        Diagnostic diagnostic = Assert.Single(RuleHarness.RunOn(
            new NamespaceFolderAnalyzer(),
            Compile(@"c:\src\Sample.Layer1\Ingest\Reader.cs", "namespace Sample.Layer1 { public sealed class Reader { } }"),
            properties));

        // The drive letter's case is not a distinction; the relative path is shown with forward slashes.
        Assert.Contains("is declared in 'Ingest/Reader.cs'", diagnostic.GetMessage(), StringComparison.Ordinal);
    }

    [Fact]
    public void The_comparison_is_case_sensitive()
    {
        Assert.Single(Run("Ingest/Reader.cs", "namespace Sample.Layer1.ingest { public sealed class Reader { } }"));
    }

    [Fact]
    public void Without_ProjectDir_the_rule_is_silent()
    {
        Assert.Empty(RuleHarness.RunOn(
            new NamespaceFolderAnalyzer(),
            Compile("Ingest/Reader.cs", "namespace Other { public sealed class Reader { } }"),
            new Dictionary<string, string>(StringComparer.Ordinal)));
    }

    [Fact]
    public void Nested_blocks_are_checked_by_their_full_name()
    {
        // The outer block holds only a nested one and is not itself reported; the inner one is
        // checked as Sample.Layer1.Ingest, which is what the folder names.
        Assert.Empty(Run("Ingest/Reader.cs", "namespace Sample.Layer1 { namespace Ingest { public sealed class Reader { } } }"));

        // The inner block's full name is Sample.Ingest, and its own part cannot be renamed under
        // Sample to reach Sample.Layer1.Ingest... it can: 'Layer1.Ingest'.
        Diagnostic inner = Assert.Single(Run("Ingest/Reader.cs", "namespace Sample { namespace Ingest { public sealed class Reader { } } }"));
        Assert.Contains("namespace 'Sample.Ingest' is declared in", inner.GetMessage(), StringComparison.Ordinal);
        Assert.Equal("Ingest", inner.Location.SourceTree!.GetText(TestContext.Current.CancellationToken).ToString(inner.Location.SourceSpan));
        Assert.Equal("Layer1.Ingest", inner.Properties[NewName]);

        // An outer block with types of its own is reported for them, and the inner block for its.
        ImmutableArray<Diagnostic> both = Run("Ingest/Reader.cs", "namespace Other { public sealed class A { } namespace Inner { public sealed class B { } } }");
        Assert.Equal(2, both.Length);
        Assert.Contains(both, d => d.GetMessage().Contains("namespace 'Other' is declared", StringComparison.Ordinal));
        Diagnostic nested = Assert.Single(both, d => d.GetMessage().Contains("namespace 'Other.Inner' is declared", StringComparison.Ordinal));

        // Not mechanical: no name under 'Other' is 'Sample.Layer1.Ingest', so no fix is offered.
        Assert.False(nested.Properties.ContainsKey(NewName));
    }

    [Fact]
    public void DesignDecision_on_every_type_in_the_declaration_answers_it()
    {
        const string Attribute = "[global::DecisionDriven.DesignDecision(typeof(object), Scope = global::DecisionDriven.ExceptionScope.Migration)]";

        Assert.Empty(RunWithMarkers("Ingest/Reader.cs",
            "namespace Sample.Layer1 { " + Attribute + " public sealed class Reader { } " + Attribute + " public sealed class Writer { } }"));

        // A declaration where only some types carry it still holds a type nobody decided about.
        Assert.Single(RunWithMarkers("Ingest/Reader.cs",
            "namespace Sample.Layer1 { " + Attribute + " public sealed class Reader { } public sealed class Writer { } }"));

        // A global type with the attribute is answered the same way.
        Assert.Empty(RunWithMarkers("Ingest/Reader.cs", Attribute + " public sealed class Reader { }"));
    }

    /// <summary>
    /// The marker attributes come in a second file outside the project directory, as a linked file
    /// would, so that their own namespace is not what the test reports on.
    /// </summary>
    private static ImmutableArray<Diagnostic> RunWithMarkers(string relativePath, string source) =>
        RuleHarness.RunOn(
            new NamespaceFolderAnalyzer(),
            CSharpCompilation.Create(
                "Sample.Layer1",
                new[]
                {
                    CSharpSyntaxTree.ParseText(source, new CSharpParseOptions(LanguageVersion.Latest), ProjectDir + relativePath),
                    CSharpSyntaxTree.ParseText(ContractSource.Markers, new CSharpParseOptions(LanguageVersion.Latest), "/elsewhere/Markers.cs"),
                },
                RuleHarness.PlatformReferences(),
                new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary)),
            Properties());

    [Fact]
    public void The_message_is_exactly_this()
    {
        // DiagnosticMessages.ExactMessageTested.
        Assert.Equal(
            "namespace 'Sample.Layer1' is declared in 'Ingest/Reader.cs', whose folder names 'Sample.Layer1.Ingest'. "
            + "Decide: declare 'Sample.Layer1.Ingest', or move the file to the folder 'Sample.Layer1' names "
            + "| mark it [DesignDecision(typeof(<Set>.<Key>), Scope = ExceptionScope.<Scope>)] "
            + "citing the accepted decision that says so. "
            + "Do not add the attribute without a decision that answers this; if the reason is only that "
            + "the code already looked like this, take the design change.",
            Assert.Single(Run("Ingest/Reader.cs", "namespace Sample.Layer1 { public sealed class Reader { } }")).GetMessage());
    }

    [Fact]
    public async Task ProjectDir_and_RootNamespace_arrive_through_the_analyzer_config_file()
    {
        // The path a build takes: both properties are SDK compiler-visible, written into the
        // generated analyzer config and read back by the compiler's parser.
        KeyValuePair<string, string>[] properties =
        {
            new KeyValuePair<string, string>("ProjectDir", "/0/"),
            new KeyValuePair<string, string>("RootNamespace", "Sample.Layer1"),
        };

        await RuleHarness.VerifyThroughAnalyzerConfigAsync<NamespaceFolderAnalyzer>(
            "namespace Sample.Layer1.Ingest { public sealed class Reader { } }",
            "/0/Ingest/Reader.cs",
            properties,
            Array.Empty<RuleHarness.Referenced>());

        await RuleHarness.VerifyThroughAnalyzerConfigAsync<NamespaceFolderAnalyzer>(
            "namespace {|#0:Sample.Layer1|} { public sealed class Reader { } }",
            "/0/Ingest/Reader.cs",
            properties,
            Array.Empty<RuleHarness.Referenced>(),
            new DiagnosticResult("DD0022", DiagnosticSeverity.Error)
                .WithLocation(0)
                .WithMessage(
                    "namespace 'Sample.Layer1' is declared in 'Ingest/Reader.cs', whose folder names 'Sample.Layer1.Ingest'. "
                    + "Decide: declare 'Sample.Layer1.Ingest', or move the file to the folder 'Sample.Layer1' names "
                    + "| mark it [DesignDecision(typeof(<Set>.<Key>), Scope = ExceptionScope.<Scope>)] "
                    + "citing the accepted decision that says so. "
                    + "Do not add the attribute without a decision that answers this; if the reason is only that "
                    + "the code already looked like this, take the design change."));
    }

    private static ImmutableArray<Diagnostic> Run(string relativePath, string source) =>
        RuleHarness.RunOn(new NamespaceFolderAnalyzer(), Compile(relativePath, source), Properties());

    private static Dictionary<string, string> Properties() =>
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["build_property.ProjectDir"] = ProjectDir,
        };

    /// <summary>One file at <paramref name="path"/>, under the project directory unless the path is rooted.</summary>
    private static CSharpCompilation Compile(string path, string source) =>
        CSharpCompilation.Create(
            "Sample.Layer1",
            new[]
            {
                CSharpSyntaxTree.ParseText(
                    source,
                    new CSharpParseOptions(LanguageVersion.Latest),
                    path.StartsWith("/", StringComparison.Ordinal) || path.Contains(':', StringComparison.Ordinal) ? path : ProjectDir + path),
            },
            RuleHarness.PlatformReferences(),
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
}
