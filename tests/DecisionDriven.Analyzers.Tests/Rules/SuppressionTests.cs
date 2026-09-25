using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using DecisionDriven.Analyzers.Rules;
using DecisionDriven.Analyzers.Tests.Ledger;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Xunit;

namespace DecisionDriven.Analyzers.Tests.Rules;

/// <summary>
/// DD0008: a DecisionDriven rule is not silenced, by any of the three ways of silencing one.
/// </summary>
public sealed class SuppressionTests
{
    [Fact]
    public void A_pragma_disabling_a_DD_rule_is_reported()
    {
        Diagnostic diagnostic = Assert.Single(Run(
            "#pragma warning disable DD0001" + Environment.NewLine
            + "namespace Consumer { public sealed class Thing { } }"));

        Assert.Equal("DD0008", diagnostic.Id);
        Assert.Equal(DiagnosticSeverity.Error, diagnostic.Severity);
    }

    [Fact]
    public void A_pragma_restoring_a_DD_rule_is_reported()
    {
        // Half of a pair, and the pair is the suppression. Reporting only the disable would leave
        // "restore" reading as an undo to anyone skimming a diff.
        Diagnostic diagnostic = Assert.Single(Run(
            "namespace Consumer { public sealed class Thing { } }" + Environment.NewLine
            + "#pragma warning restore DD0001"));

        Assert.Equal("DD0008", diagnostic.Id);
        Assert.Contains("so something disabled it", diagnostic.GetMessage(), StringComparison.Ordinal);
    }

    [Fact]
    public void A_pragma_in_an_inactive_branch_is_not_reported()
    {
        // It suppresses nothing in this compilation. The build that defines the symbol activates
        // it, and that build reports it - the next test.
        Assert.Empty(Run(
            "#if SOMETHING_NOT_DEFINED" + Environment.NewLine
            + "#pragma warning disable DD0001" + Environment.NewLine
            + "#endif" + Environment.NewLine
            + "namespace Consumer { public sealed class Thing { } }"));
    }

    [Fact]
    public void A_pragma_in_an_active_branch_is_reported()
    {
        Assert.Single(Run(
            "#define SOMETHING" + Environment.NewLine
            + "#if SOMETHING" + Environment.NewLine
            + "#pragma warning disable DD0001" + Environment.NewLine
            + "#endif" + Environment.NewLine
            + "namespace Consumer { public sealed class Thing { } }"));
    }

    [Theory]
    [InlineData("Stub.g.cs")]
    [InlineData("Stub.generated.cs")]
    [InlineData("Stub.Designer.cs")]
    public void A_pragma_in_generated_code_is_reported(string path)
    {
        // Every other rule skips generated code, so a generated
        // file is exactly where a suppression would go unseen if this rule skipped it as well.
        CSharpCompilation compilation = Compile(
            path,
            "#pragma warning disable DD0018" + Environment.NewLine
            + "namespace Consumer { public sealed class Thing { } }");

        Diagnostic diagnostic = Assert.Single(RuleHarness.RunOn(new SuppressionAnalyzer(), compilation));

        Assert.Equal("DD0008", diagnostic.Id);
        Assert.Equal(path, diagnostic.Location.SourceTree!.FilePath);
    }

    [Fact]
    public void A_pragma_naming_a_compiler_warning_is_not_reported()
    {
        Assert.Empty(Run(
            "#pragma warning disable CS0168" + Environment.NewLine
            + "namespace Consumer { public sealed class Thing { } }"));
    }

    [Theory]
    [InlineData("DDBUILD0001", "DDBUILD")]
    [InlineData("DDGEN0001", "DDGEN")]
    [InlineData("DD0004", "DD")]
    public void Every_id_family_this_package_ships_is_covered(string id, string family)
    {
        // RuleTiers.IdFamilies. DD is a prefix of the other two, so a membership test that matched
        // it first would name the family wrong in the message even while reporting the right line.
        Diagnostic diagnostic = Assert.Single(Run(
            "#pragma warning disable " + id + Environment.NewLine
            + "namespace Consumer { public sealed class Thing { } }"));

        Assert.Contains("a " + family + " rule", diagnostic.GetMessage(), StringComparison.Ordinal);
    }

    [Fact]
    public void A_product_prefix_named_in_editorconfig_is_covered()
    {
        // TwoPackages.RuleIdPrefixDD leaves a product's prefix to the product, and this package
        // cannot know it. TwoPackages.ConfigurationViaMsBuildProperties allows .editorconfig
        // options, which is where it is read from.
        string source = "#pragma warning disable ACME0001" + Environment.NewLine
            + "namespace Consumer { public sealed class Thing { } }";

        Assert.Empty(Run(source));

        Diagnostic diagnostic = Assert.Single(Run(
            source,
            new Dictionary<string, string>(StringComparer.Ordinal) { ["dd_rule_id_prefixes"] = "ACME, CONTOSO" }));

        Assert.Contains("a ACME rule", diagnostic.GetMessage(), StringComparison.Ordinal);
    }

    [Fact]
    public void A_pragma_does_silence_an_ordinary_rule()
    {
        // The control for the test below. Without it, "DD0008 survives its own pragma" would pass
        // just as well on a harness that never applied pragmas at all, and would prove nothing.
        string source = "namespace Consumer { public sealed class Thing { public static int Counter; } }";

        Assert.Single(RuleHarness.Run(new StaticStateAnalyzer(), source, assemblyName: "Consumer"));

        Assert.Empty(RuleHarness.Run(
            new StaticStateAnalyzer(),
            "#pragma warning disable DD0004" + Environment.NewLine + source,
            assemblyName: "Consumer"));
    }

    [Fact]
    public void A_pragma_disabling_DD0008_itself_is_reported()
    {
        // The rule would be a comment otherwise. Its descriptor is NotConfigurable, so neither this
        // pragma nor any other reaches it - and the test above shows the pragma would have worked.
        Diagnostic diagnostic = Assert.Single(Run(
            "#pragma warning disable DD0008" + Environment.NewLine
            + "namespace Consumer { public sealed class Thing { } }"));

        Assert.Equal("DD0008", diagnostic.Id);
    }

    [Fact]
    public void A_SuppressMessage_naming_a_DD_rule_is_reported()
    {
        Diagnostic diagnostic = Assert.Single(Run(
            "namespace Consumer { public sealed class Thing { "
            + "[System.Diagnostics.CodeAnalysis.SuppressMessage(\"DecisionDriven\", \"DD0004:Mutable static state\")] "
            + "public static int Counter; } }"));

        Assert.Equal("DD0008", diagnostic.Id);
        Assert.Contains("[SuppressMessage] silences DD0004", diagnostic.GetMessage(), StringComparison.Ordinal);
    }

    [Fact]
    public void A_SuppressMessage_naming_another_analyzer_is_not_reported()
    {
        Assert.Empty(Run(
            "namespace Consumer { public sealed class Thing { "
            + "[System.Diagnostics.CodeAnalysis.SuppressMessage(\"Style\", \"IDE0044:Add readonly modifier\")] "
            + "public static int Counter; } }"));
    }

    [Fact]
    public void An_editorconfig_severity_below_the_declared_tier_is_reported()
    {
        Diagnostic diagnostic = Assert.Single(Run(
            Empty,
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["dotnet_diagnostic.DD0001.severity"] = "warning",
            }));

        Assert.Equal("DD0008", diagnostic.Id);
        Assert.Contains("below the 'error' its tier declares", diagnostic.GetMessage(), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("error")]
    [InlineData("default")]
    public void An_editorconfig_severity_that_is_not_a_downgrade_is_not_reported(string severity)
    {
        Assert.Empty(Run(
            Empty,
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["dotnet_diagnostic.DD0001.severity"] = severity,
            }));
    }

    [Fact]
    public void An_editorconfig_entry_for_DD0008_is_not_reported()
    {
        // It has no effect: the descriptor is NotConfigurable. Reporting a line that changes
        // nothing would send somebody to delete the one entry that was already harmless.
        Assert.Empty(Run(
            Empty,
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["dotnet_diagnostic.DD0008.severity"] = "none",
            }));
    }

    [Fact]
    public void A_clean_file_is_not_reported()
    {
        Assert.Empty(Run(Empty));
    }

    [Fact]
    public void The_message_is_exactly_this()
    {
        // DiagnosticMessages.ExactMessageTested.
        Diagnostic diagnostic = Assert.Single(Run(
            "#pragma warning disable DD0004" + Environment.NewLine
            + "namespace Consumer { public sealed class Thing { } }"));

        Assert.Equal(
            "#pragma warning disable silences DD0004, a DD rule. "
            + "Decide: delete the pragma and answer DD0004 where it reports: change the design, or "
            + "mark the symbol [DesignDecision(typeof(<Set>.<Key>), Scope = ExceptionScope.<Scope>)] "
            + "citing an accepted decision "
            + "| change the rule itself, by superseding the decision that set its tier in "
            + "DecisionDriven.Analyzers. Do not reach for a suppression to get to green; if the "
            + "reason is only that the code already looked like this, take the design change.",
            diagnostic.GetMessage());
    }

    // RuleTiers.GeneratedCodeIsExempt: code is generated only if a source generator produced it in
    // this compilation, its file is named *.g.cs, *.generated.cs or *.designer.cs, or it lies in the
    // project's intermediate output directory. Anything else that asks Roslyn to treat code as
    // generated is a request for every other rule to look away, and is reported here.

    private const string Guard =
        " | change the rule itself, by superseding the decision that set its tier in "
        + "DecisionDriven.Analyzers. Do not reach for a suppression to get to green; if the reason is "
        + "only that the code already looked like this, take the design change.";

    private const string ToolPath =
        "if a tool writes this file, have it name the file *.g.cs, *.generated.cs or *.designer.cs, or "
        + "write it to the intermediate output directory";

    [Fact]
    public void An_auto_generated_header_on_a_hand_written_file_is_reported_exactly()
    {
        Diagnostic diagnostic = Assert.Single(RuleHarness.RunOn(
            new SuppressionAnalyzer(),
            Compile("Hand.cs", "// <auto-generated/>" + Environment.NewLine + Empty)));

        Assert.Equal(
            "An <auto-generated> header hides Hand.cs from every DD rule, and Hand.cs is not generated "
            + "code. Decide: delete the header and answer what the rules report; " + ToolPath + Guard,
            diagnostic.GetMessage());
        Assert.Equal(0, diagnostic.Location.SourceSpan.Start);
    }

    [Theory]
    [InlineData("//------------------------------------------------------------------------------\n// <auto-generated>\n//     This code was generated by a tool.\n// </auto-generated>\n")]
    [InlineData("/* <autogenerated /> */\n")]
    [InlineData("// Copyright\n\n// <AUTO-GENERATED/>\n")]
    public void Every_form_of_the_header_Roslyn_honours_is_reported(string header)
    {
        // Roslyn looks at every comment before the first token, for either spelling.
        Diagnostic diagnostic = Assert.Single(RuleHarness.RunOn(new SuppressionAnalyzer(), Compile("Hand.cs", header + Empty)));

        Assert.StartsWith("An <auto-generated> header hides Hand.cs", diagnostic.GetMessage(), StringComparison.Ordinal);
    }

    [Fact]
    public void A_header_after_the_first_token_is_not_reported()
    {
        // Roslyn does not read it there, so it hides nothing.
        Assert.Empty(RuleHarness.RunOn(
            new SuppressionAnalyzer(),
            Compile("Hand.cs", "namespace Consumer { // <auto-generated/>" + Environment.NewLine + "public sealed class Thing { } }")));
    }

    [Fact]
    public void GeneratedCode_on_a_hand_written_type_is_reported_exactly()
    {
        Diagnostic diagnostic = Assert.Single(RuleHarness.RunOn(
            new SuppressionAnalyzer(),
            Compile(
                "Hand.cs",
                "namespace Consumer { [System.CodeDom.Compiler.GeneratedCode(\"tool\", \"1.0\")] public sealed class Thing { } }")));

        Assert.Equal(
            "[GeneratedCode] hides what it marks in Hand.cs from every DD rule, and Hand.cs is not "
            + "generated code. Decide: delete the attribute and answer what the rules report; " + ToolPath + Guard,
            diagnostic.GetMessage());
    }

    [Theory]
    [InlineData("namespace Consumer { public sealed class Thing { [System.CodeDom.Compiler.GeneratedCode(\"t\", \"1\")] public void Run() { } } }")]
    [InlineData("namespace Consumer { public sealed class Thing { [System.CodeDom.Compiler.GeneratedCodeAttribute(\"t\", \"1\")] public int Value { get; init; } } }")]
    [InlineData("using G = System.CodeDom.Compiler.GeneratedCodeAttribute; namespace Consumer { [G(\"t\", \"1\")] public interface IThing { } }")]
    [InlineData("[assembly: System.CodeDom.Compiler.GeneratedCode(\"t\", \"1\")] namespace Consumer { public sealed class Thing { } }")]
    public void GeneratedCode_on_any_symbol_of_a_hand_written_file_is_reported(string source)
    {
        // Bound, not matched by name: an alias or the full attribute name hides a symbol just as well.
        Diagnostic diagnostic = Assert.Single(RuleHarness.RunOn(new SuppressionAnalyzer(), Compile("Hand.cs", source)));

        Assert.StartsWith("[GeneratedCode] hides what it marks in Hand.cs", diagnostic.GetMessage(), StringComparison.Ordinal);
    }

    [Fact]
    public void An_attribute_named_GeneratedCode_that_is_not_the_BCLs_is_not_reported()
    {
        Assert.Empty(RuleHarness.RunOn(
            new SuppressionAnalyzer(),
            Compile(
                "Hand.cs",
                "namespace Consumer { [System.AttributeUsage(System.AttributeTargets.All)] public sealed class GeneratedCodeAttribute : System.Attribute { } [GeneratedCode] public sealed class Thing { } }")));
    }

    [Fact]
    public void Editorconfig_generated_code_covering_a_hand_written_file_is_reported_exactly()
    {
        Diagnostic diagnostic = Assert.Single(RuleHarness.RunOn(
            new SuppressionAnalyzer(),
            Compile("Hand.cs", Empty),
            fileOptions: For("Hand.cs", "generated_code", "true")));

        Assert.Equal(
            ".editorconfig sets generated_code = true for Hand.cs, which hides it from every DD rule, and "
            + "Hand.cs is not generated code. Decide: remove the setting and answer what the rules "
            + "report; " + ToolPath + Guard,
            diagnostic.GetMessage());
    }

    [Theory]
    [InlineData("Other.cs", "true")]
    [InlineData("Hand.cs", "false")]
    [InlineData("Hand.cs", "auto")]
    public void Editorconfig_generated_code_that_does_not_mark_this_file_is_not_reported(string coveredPath, string value)
    {
        // A section that does not cover the file says nothing about it, and false or auto asks
        // nobody to look away.
        Assert.Empty(RuleHarness.RunOn(
            new SuppressionAnalyzer(),
            Compile("Hand.cs", Empty),
            fileOptions: For(coveredPath, "generated_code", value)));
    }

    [Fact]
    public void A_name_only_Roslyn_counts_as_generated_is_reported_exactly()
    {
        Diagnostic diagnostic = Assert.Single(RuleHarness.RunOn(new SuppressionAnalyzer(), Compile("Hand.g.i.cs", Empty)));

        Assert.Equal(
            "The name Hand.g.i.cs makes Roslyn treat the file as generated, which hides it from every DD "
            + "rule, and Hand.g.i.cs is not generated code. Decide: rename the file and answer what the "
            + "rules report; " + ToolPath + Guard,
            diagnostic.GetMessage());
    }

    [Theory]
    [InlineData("TemporaryGeneratedFile_Hand.cs")]
    [InlineData("Hand.G.I.cs")]
    public void Every_name_only_Roslyn_counts_as_generated_is_reported(string path)
    {
        Diagnostic diagnostic = Assert.Single(RuleHarness.RunOn(new SuppressionAnalyzer(), Compile(path, Empty)));

        Assert.StartsWith("The name " + path, diagnostic.GetMessage(), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("Stub.g.cs")]
    [InlineData("Stub.generated.cs")]
    [InlineData("Stub.Designer.cs")]
    [InlineData("STUB.G.CS")]
    public void All_three_requests_in_a_file_named_as_generated_are_not_reported(string path)
    {
        Assert.Empty(RuleHarness.RunOn(
            new SuppressionAnalyzer(),
            Compile(path, "// <auto-generated/>" + Environment.NewLine + LookAwaySource),
            fileOptions: For(path, "generated_code", "true")));
    }

    [Fact]
    public void All_three_requests_in_the_intermediate_output_directory_are_not_reported()
    {
        // Where build tasks write what they generate: AssemblyInfo.cs, a test platform's entry point.
        string project = ProjectDirectory();
        string path = System.IO.Path.Combine(project, "obj", "Release", "net10.0", "Consumer.AssemblyInfo.cs");

        Assert.Empty(RuleHarness.RunOn(
            new SuppressionAnalyzer(),
            Compile(path, "// <auto-generated/>" + Environment.NewLine + LookAwaySource),
            BuildProperties(project),
            fileOptions: For(path, "generated_code", "true")));
    }

    [Fact]
    public void The_same_file_outside_the_intermediate_output_directory_is_reported()
    {
        string project = ProjectDirectory();
        string path = System.IO.Path.Combine(project, "Consumer.AssemblyInfo.cs");

        ImmutableArray<Diagnostic> diagnostics = RuleHarness.RunOn(
            new SuppressionAnalyzer(),
            Compile(path, "// <auto-generated/>" + Environment.NewLine + LookAwaySource),
            BuildProperties(project),
            fileOptions: For(path, "generated_code", "true"));

        Assert.Equal(3, diagnostics.Length);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void GeneratedCode_on_this_packages_generated_decision_types_is_not_reported(bool realBuild)
    {
        // DD0007 reads [GeneratedCode] on the generator's output as half of its provenance check, so
        // reporting it would make every citation of a decision a DD0008 finding. The generator names its
        // hints *.g.cs, so the name decides this before the anchor does; the next-but-two test is the
        // one that needs the anchor.
        GeneratorHarness.Result result = Generate(realBuild ? GeneratorBase() : null);

        Assert.Contains(
            result.Compilation.SyntaxTrees,
            tree => tree.ToString().Contains("[global::System.CodeDom.Compiler.GeneratedCode(\"DecisionDriven.Analyzers\"", StringComparison.Ordinal));
        Assert.Empty(RuleHarness.RunOn(new SuppressionAnalyzer(), result.Compilation));
    }

    [Fact]
    public void The_generated_text_copied_into_a_hand_written_file_is_reported()
    {
        // Provenance is where the tree came from, not what it says.
        GeneratorHarness.Result result = Generate(GeneratorBase());
        string decisions = result.GeneratedSource("Decisions.g.cs")
            ?? result.Compilation.SyntaxTrees.Select(t => t.ToString()).First(t => t.Contains("GeneratedCode", StringComparison.Ordinal));

        Compilation forged = result.Compilation.AddSyntaxTrees(CSharpSyntaxTree.ParseText(
            decisions.Replace("namespace ", "namespace Forged.", StringComparison.Ordinal),
            new CSharpParseOptions(LanguageVersion.Latest),
            System.IO.Path.Combine(ProjectDirectory(), "Forged.cs"),
            cancellationToken: TestContext.Current.CancellationToken));

        Assert.Contains(
            RuleHarness.RunOn(new SuppressionAnalyzer(), forged, allowCompileErrors: true),
            d => d.Location.SourceTree!.FilePath.EndsWith("Forged.cs", StringComparison.Ordinal)
                && d.GetMessage().StartsWith("[GeneratedCode]", StringComparison.Ordinal));
    }

    [Fact]
    public void A_file_shaped_like_generator_output_outside_this_runs_root_is_reported()
    {
        // The shape <assembly>/<generator type>/<hint> is two directories anyone can create.
        GeneratorHarness.Result result = Generate(GeneratorBase());
        string path = System.IO.Path.Combine(ProjectDirectory(), "Some.Generator", "Some.Generator.Type", "Output.cs");

        Compilation forged = result.Compilation.AddSyntaxTrees(CSharpSyntaxTree.ParseText(
            "// <auto-generated/>" + Environment.NewLine + "namespace Forged { public sealed class Thing { } }",
            new CSharpParseOptions(LanguageVersion.Latest),
            path,
            cancellationToken: TestContext.Current.CancellationToken));

        Diagnostic diagnostic = Assert.Single(RuleHarness.RunOn(new SuppressionAnalyzer(), forged));
        Assert.Equal(path, diagnostic.Location.SourceTree!.FilePath);
    }

    [Fact]
    public void Another_generators_output_under_this_runs_root_is_not_reported()
    {
        // Every generator's output shares the root; the anchor is where ours went, not ours alone.
        string root = GeneratorBase();
        GeneratorHarness.Result result = Generate(root);
        string path = System.IO.Path.Combine(root, "Other.Generators", "Other.Generators.RegexGenerator", "Regex.cs");

        Compilation withOther = result.Compilation.AddSyntaxTrees(CSharpSyntaxTree.ParseText(
            "// <auto-generated/>" + Environment.NewLine + LookAwaySource.Replace("Consumer", "Other", StringComparison.Ordinal),
            new CSharpParseOptions(LanguageVersion.Latest),
            path,
            cancellationToken: TestContext.Current.CancellationToken));

        Assert.Empty(RuleHarness.RunOn(new SuppressionAnalyzer(), withOther));
    }

    private const string LookAwaySource =
        "namespace Consumer { [System.CodeDom.Compiler.GeneratedCode(\"tool\", \"1.0\")] public sealed class Thing { } }";

    private static CSharpCompilation Compile(string path, string source) =>
        CSharpCompilation.Create(
            "Consumer",
            new[] { CSharpSyntaxTree.ParseText(source, new CSharpParseOptions(LanguageVersion.Latest), path) },
            RuleHarness.PlatformReferences(),
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

    private static Dictionary<string, Dictionary<string, string>> For(string path, string key, string value) =>
        new Dictionary<string, Dictionary<string, string>>(StringComparer.Ordinal)
        {
            [path] = new Dictionary<string, string>(StringComparer.Ordinal) { [key] = value },
        };

    /// <summary>An absolute project directory on any platform; the compiler never sees a relative one.</summary>
    private static string ProjectDirectory() => System.IO.Path.Combine(System.IO.Path.GetTempPath(), "src", "Consumer");

    private static string GeneratorBase() => System.IO.Path.Combine(ProjectDirectory(), "obj", "generated");

    /// <summary>What MSBuild hands over: ProjectDir with a trailing separator, the intermediate path relative.</summary>
    private static Dictionary<string, string> BuildProperties(string project) =>
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["build_property.ProjectDir"] = project + System.IO.Path.DirectorySeparatorChar,
            ["build_property.IntermediateOutputPath"] = "obj" + System.IO.Path.DirectorySeparatorChar + "Release"
                + System.IO.Path.DirectorySeparatorChar + "net10.0" + System.IO.Path.DirectorySeparatorChar,
        };

    private static GeneratorHarness.Result Generate(string? baseDirectory) =>
        GeneratorHarness.Run(
            "namespace Consumer { }",
            new List<GeneratorHarness.LedgerFile>
            {
                LedgerInput.AsSet(LedgerInput.FrontMatter("store", "StoreIsAContract", "The store's read side is a contract")),
            },
            baseDirectory: baseDirectory);

    private const string Empty = "namespace Consumer { public sealed class Thing { } }";

    private static ImmutableArray<Diagnostic> Run(string source, Dictionary<string, string>? editorConfig = null) =>
        RuleHarness.Run(new SuppressionAnalyzer(), source, assemblyName: "Consumer", editorConfig: editorConfig);
}
