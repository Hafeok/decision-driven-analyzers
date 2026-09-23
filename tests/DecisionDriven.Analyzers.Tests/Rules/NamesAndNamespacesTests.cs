using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using DecisionDriven.Analyzers.Rules;
using Microsoft.CodeAnalysis;
using Xunit;

namespace DecisionDriven.Analyzers.Tests.Rules;

/// <summary>
/// DD0005: no grab-bag names on an assembly or a namespace.
/// </summary>
public sealed class BannedNameTests
{
    [Fact]
    public void A_namespace_named_for_what_it_does_is_not_reported()
    {
        Assert.Empty(Run("namespace Sample.Layer1.Ingest { public sealed class Reader { } }"));
    }

    [Theory]
    [InlineData("Common")]
    [InlineData("Core")]
    [InlineData("Utils")]
    [InlineData("Utilities")]
    [InlineData("Helpers")]
    [InlineData("Abstractions")]
    [InlineData("Shared")]
    [InlineData("Misc")]
    [InlineData("Extensions")]
    public void A_grab_bag_namespace_segment_is_reported(string segment)
    {
        Diagnostic diagnostic = Assert.Single(Run($"namespace Sample.Layer1.{segment} {{ public sealed class Thing {{ }} }}"));

        Assert.Equal("DD0005", diagnostic.Id);
        Assert.Equal(DiagnosticSeverity.Error, diagnostic.Severity);
        Assert.Contains(segment, diagnostic.GetMessage(), StringComparison.Ordinal);
    }

    [Fact]
    public void A_type_named_Extensions_is_not_reported()
    {
        // ADR-A06 bans Extensions as a namespace and not as a type name: StringExtensions says what
        // it extends, an Extensions namespace says only that somebody had nowhere else to put things.
        Assert.Empty(Run("namespace Sample.Layer1 { public static class StringExtensions { } }"));
    }

    [Fact]
    public void An_internal_type_in_a_banned_namespace_is_not_reported()
    {
        // Only a public surface is a package's shape. 'Internal' is banned as a *public* namespace,
        // which is the case this distinguishes.
        Assert.Empty(Run("namespace Sample.Layer1.Internal { internal sealed class Thing { } }"));
    }

    [Fact]
    public void A_public_type_in_an_Internal_namespace_is_reported()
    {
        Diagnostic diagnostic = Assert.Single(Run("namespace Sample.Layer1.Internal { public sealed class Thing { } }"));

        Assert.Contains("Internal", diagnostic.GetMessage(), StringComparison.Ordinal);
        Assert.Contains("make the types in it internal", diagnostic.GetMessage(), StringComparison.Ordinal);
    }

    [Fact]
    public void A_banned_segment_in_the_assembly_name_is_reported()
    {
        ImmutableArray<Diagnostic> diagnostics = RuleHarness.Run(
            new BannedNameAnalyzer(),
            "namespace Sample.Common { public sealed class Thing { } }",
            assemblyName: "Sample.Common");

        // Two findings, because there are two names: the assembly is called Common and so is the
        // namespace. One rename fixes both, and neither finding is redundant - an assembly named
        // Sample.Common whose types live in Sample.Ingest would produce only the first.
        Assert.Equal(2, diagnostics.Length);
        Assert.Contains(diagnostics, d => d.GetMessage().Contains("assembly 'Sample.Common'", StringComparison.Ordinal));
        Assert.Contains(diagnostics, d => d.GetMessage().Contains("namespace 'Sample.Common'", StringComparison.Ordinal));
    }

    [Fact]
    public void The_list_is_configurable()
    {
        // NamesAndNamespaces.BannedGrabBagNames says the list is configurable. A consumer whose
        // domain genuinely has a 'Core' can say so, and can ban something of its own instead.
        Dictionary<string, string> options = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            [BannedNameAnalyzer.OptionName] = "Grabbag, Sundry",
        };

        Assert.Empty(RuleHarness.Run(
            new BannedNameAnalyzer(),
            "namespace Sample.Layer1.Core { public sealed class Thing { } }",
            assemblyName: "Sample.Layer1",
            editorConfig: options));

        Assert.Single(RuleHarness.Run(
            new BannedNameAnalyzer(),
            "namespace Sample.Layer1.Sundry { public sealed class Thing { } }",
            assemblyName: "Sample.Layer1",
            editorConfig: options));
    }

    [Fact]
    public void The_message_for_the_canonical_violating_sample_is_exact()
    {
        Diagnostic diagnostic = Assert.Single(Run("namespace Sample.Layer1.Utils { public sealed class Thing { } }"));

        Assert.Equal(
            "public type 'Thing' is in namespace 'Sample.Layer1.Utils', whose segment 'Utils' is a grab-bag name. "
            + "Decide: name the namespace for what the types in it are responsible for, or move them "
            + "into the namespace of the thing they belong to "
            + "| mark it [DesignDecision(typeof(<Set>.<Key>), Scope = ExceptionScope.<Scope>)] "
            + "citing the accepted decision that says so. "
            + "Do not add the attribute without a decision that answers this; "
            + "if the reason is only that the code already looked like this, take the design change.",
            diagnostic.GetMessage());
    }

    private static ImmutableArray<Diagnostic> Run(string source) =>
        RuleHarness.Run(new BannedNameAnalyzer(), source, assemblyName: "Sample.Layer1");
}

/// <summary>
/// DD0006: every public type lives under a root namespace equal to the assembly name.
/// </summary>
public sealed class RootNamespaceTests
{
    [Fact]
    public void A_type_under_the_root_namespace_is_not_reported()
    {
        Assert.Empty(Run("namespace Sample.Layer1 { public sealed class Thing { } }"));
        Assert.Empty(Run("namespace Sample.Layer1.Ingest { public sealed class Thing { } }"));
    }

    [Fact]
    public void A_public_type_outside_the_root_namespace_is_reported()
    {
        Diagnostic diagnostic = Assert.Single(Run("namespace Other.Place { public sealed class Thing { } }"));

        Assert.Equal("DD0006", diagnostic.Id);
        Assert.Equal(DiagnosticSeverity.Error, diagnostic.Severity);
    }

    [Fact]
    public void A_public_type_in_the_global_namespace_is_reported()
    {
        Diagnostic diagnostic = Assert.Single(Run("public sealed class Thing { }"));

        Assert.Contains("global namespace", diagnostic.GetMessage(), StringComparison.Ordinal);
    }

    [Fact]
    public void An_internal_type_outside_the_root_namespace_is_not_reported()
    {
        // The rule is about the package's shape, which is its public surface.
        Assert.Empty(Run("namespace Other.Place { internal sealed class Thing { } }"));
    }

    [Fact]
    public void A_namespace_that_merely_starts_with_the_assembly_name_is_reported()
    {
        // 'Sample.Layer10' is not under 'Sample.Layer1'. Matching the prefix without the dot would
        // let a neighbouring assembly's namespace pass.
        Assert.Single(Run("namespace Sample.Layer10 { public sealed class Thing { } }"));
    }

    [Fact]
    public void A_nested_public_type_is_checked_through_the_type_that_contains_it()
    {
        // One finding, not two: the nested type takes its namespace from the outer one.
        Assert.Single(Run("namespace Other.Place { public sealed class Outer { public sealed class Inner { } } }"));
    }

    [Fact]
    public void The_message_for_the_canonical_violating_sample_is_exact()
    {
        Diagnostic diagnostic = Assert.Single(Run("namespace Other.Place { public sealed class Thing { } }"));

        Assert.Equal(
            "public type 'Thing' is in namespace 'Other.Place', which is not under this assembly's "
            + "root namespace 'Sample.Layer1'. "
            + "Decide: move it under 'Sample.Layer1', or move it to the assembly whose name matches "
            + "the namespace it belongs in "
            + "| mark it [DesignDecision(typeof(<Set>.<Key>), Scope = ExceptionScope.<Scope>)] "
            + "citing the accepted decision that says so. "
            + "Do not add the attribute without a decision that answers this; "
            + "if the reason is only that the code already looked like this, take the design change.",
            diagnostic.GetMessage());
    }

    private static ImmutableArray<Diagnostic> Run(string source) =>
        RuleHarness.Run(new RootNamespaceAnalyzer(), source, assemblyName: "Sample.Layer1");
}
