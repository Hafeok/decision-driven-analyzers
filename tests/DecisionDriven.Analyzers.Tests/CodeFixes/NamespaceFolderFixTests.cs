using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using DecisionDriven.Analyzers.CodeFixes;
using DecisionDriven.Analyzers.Rules;
using Microsoft.CodeAnalysis.CodeActions;
using Xunit;

namespace DecisionDriven.Analyzers.Tests.CodeFixes;

/// <summary>
/// DD0022's design-change fix: declare the namespace the folder names.
/// </summary>
public sealed class NamespaceFolderFixTests
{
    private static readonly Dictionary<string, string> Options = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["build_property.ProjectDir"] = "/proj/",
    };

    [Theory]
    [InlineData("namespace Sample.Layer1\n{\n    public sealed class Reader { }\n}\n", "namespace Sample.Layer1.Ingest\n{\n    public sealed class Reader { }\n}\n")]
    [InlineData("namespace Sample.Layer1;\n\npublic sealed class Reader { }\n", "namespace Sample.Layer1.Ingest;\n\npublic sealed class Reader { }\n")]
    public void The_declaration_is_renamed_to_what_the_folder_names(string source, string expected)
    {
        string fixedSource = CodeFixHarness.ApplySingle(
            new NamespaceFolderAnalyzer(),
            new NamespaceFolderFix(),
            source,
            options: Options,
            documentPath: "/proj/Ingest/Reader.cs");

        Assert.Equal(expected, fixedSource);
    }

    [Fact]
    public void A_nested_block_is_renamed_by_its_own_part()
    {
        string fixedSource = CodeFixHarness.ApplySingle(
            new NamespaceFolderAnalyzer(),
            new NamespaceFolderFix(),
            "namespace Sample\n{\n    namespace Other\n    {\n        public sealed class Reader { }\n    }\n}\n",
            options: Options,
            documentPath: "/proj/Ingest/Reader.cs");

        Assert.Equal("namespace Sample\n{\n    namespace Layer1.Ingest\n    {\n        public sealed class Reader { }\n    }\n}\n", fixedSource);
    }

    [Fact]
    public void No_fix_is_offered_for_a_type_in_the_global_namespace()
    {
        // Wrapping a type in a declaration decides which of the file's members belong together,
        // which is not mechanical.
        ImmutableArray<CodeAction> actions = CodeFixHarness.OfferedActions(
            new NamespaceFolderAnalyzer(),
            new NamespaceFolderFix(),
            "public sealed class Reader { }",
            options: Options,
            documentPath: "/proj/Ingest/Reader.cs");

        Assert.Empty(actions);
    }

    [Fact]
    public void No_fix_is_offered_where_the_rename_is_not_mechanical()
    {
        // The inner block's full name is Other.Inner, and no name under Other is what the folder
        // names; the outer block gets its fix, the inner one none.
        ImmutableArray<CodeAction> actions = CodeFixHarness.OfferedActions(
            new NamespaceFolderAnalyzer(),
            new NamespaceFolderFix(),
            "namespace Other { public sealed class A { } namespace Inner { public sealed class B { } } }",
            options: Options,
            documentPath: "/proj/Ingest/Reader.cs");

        CodeAction action = Assert.Single(actions);
        Assert.Equal("Declare the namespace the folder names: 'Sample.Layer1.Ingest'", action.Title);
    }
}
