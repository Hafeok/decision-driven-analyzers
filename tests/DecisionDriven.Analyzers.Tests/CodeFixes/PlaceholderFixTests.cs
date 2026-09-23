using System;
using System.Collections.Immutable;
using System.Linq;
using System.Threading;
using DecisionDriven.Analyzers.CodeFixes;
using DecisionDriven.Analyzers.Rules;
using DecisionDriven.Analyzers.Tests.Rules;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CodeActions;
using Microsoft.CodeAnalysis.CodeFixes;
using Microsoft.CodeAnalysis.CSharp;
using Xunit;

namespace DecisionDriven.Analyzers.Tests.CodeFixes;

/// <summary>
/// The documented-exception fix, and the fact that it cannot reach green.
/// </summary>
/// <remarks>
/// <c>DiagnosticMessages.PlaceholderFixDoesNotCompile</c> is the load-bearing claim here. A fix that
/// produced compiling code would be a one-click way to launder any violation into an accepted one,
/// which is exactly what the guard sentence in every message exists to prevent.
/// </remarks>
public sealed class PlaceholderFixTests
{
    [Fact]
    public void The_fix_is_offered_for_DD0003_on_the_calls_containing_member()
    {
        const string Source = """
            internal sealed class Service
            {
                public object? Resolve(global::System.IServiceProvider provider) => provider.GetService(typeof(string));
            }
            """;

        string fixedSource = CodeFixHarness.ApplySingle(
            new ServiceLocationAnalyzer(),
            new DesignDecisionPlaceholderFix(),
            Source);

        Assert.Contains("DesignDecision(typeof(____.____), Scope = ExceptionScope.____)", fixedSource, StringComparison.Ordinal);

        // On the member, not on the statement: a statement cannot carry an attribute, and the
        // decision being documented is about what the member does.
        Assert.Contains("class Service", fixedSource, StringComparison.Ordinal);
        int attributeIndex = fixedSource.IndexOf("DesignDecision", StringComparison.Ordinal);
        int memberIndex = fixedSource.IndexOf("public object? Resolve", StringComparison.Ordinal);
        Assert.True(attributeIndex < memberIndex, "The placeholder should sit above the member it documents.");
    }

    [Fact]
    public void The_fixed_source_does_not_compile()
    {
        const string Source = """
            internal sealed class Service
            {
                public object? Resolve(global::System.IServiceProvider provider) => provider.GetService(typeof(string));
            }
            """;

        string fixedSource = CodeFixHarness.ApplySingle(
            new ServiceLocationAnalyzer(),
            new DesignDecisionPlaceholderFix(),
            Source);

        ImmutableArray<Diagnostic> errors = CodeFixHarness.CompileErrors(fixedSource);

        Assert.NotEmpty(errors);

        // The remaining error names the thing that is actually missing - a filed decision - rather
        // than something incidental about the syntax.
        Assert.Contains(errors, d => d.GetMessage().Contains("____", StringComparison.Ordinal));
    }

    [Fact]
    public void The_fix_is_not_offered_for_DD0001()
    {
        // DD0001 reports on the compilation: there is no declaration in any file that the reference
        // belongs to, so there is nowhere to put the attribute. Offering a fix that silently did
        // nothing, or that picked an arbitrary type, would be worse than offering none.
        ImmutableArray<CodeAction> actions = CodeFixHarness.OfferedActions(
            new LayerReferenceAnalyzer(),
            new DesignDecisionPlaceholderFix(),
            "internal sealed class Consumer { }",
            assemblyName: "Sample.Layer1",
            archFamily: "Sample",
            archLayer: 1,
            references: new[] { new RuleHarness.Referenced("Sample.Sibling", archLayer: 1) });

        Assert.Empty(actions);
    }

    [Fact]
    public void The_fix_is_offered_for_DD0002_beside_the_grant()
    {
        const string Source = """[assembly: System.Runtime.CompilerServices.InternalsVisibleTo("Sample.Layer2")]""";

        string fixedSource = CodeFixHarness.ApplySingle(
            new InternalsVisibleToAnalyzer(),
            new DesignDecisionPlaceholderFix(),
            Source);

        Assert.Contains("DesignDecision(typeof(____.____), Scope = ExceptionScope.____)", fixedSource, StringComparison.Ordinal);
        Assert.Contains("InternalsVisibleTo", fixedSource, StringComparison.Ordinal);
    }

    [Fact]
    public void The_placeholder_text_is_the_one_ADR_A14_specifies()
    {
        // The exact string matters: it is what a reader greps for, and what CLAUDE.md forbids
        // leaving in this repository's own tree at the end of a session.
        Assert.Equal(
            "DesignDecision(typeof(____.____), Scope = ExceptionScope.____)",
            DesignDecisionPlaceholderFix.Placeholder);
    }

    [Fact]
    public void There_is_no_fix_all()
    {
        // Applying this to every violation at once would turn a backlog of decision questions into
        // a file full of identical placeholders.
        Assert.Null(new DesignDecisionPlaceholderFix().GetFixAllProvider());
    }
}
