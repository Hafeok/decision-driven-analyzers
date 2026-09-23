using System;
using System.Collections.Immutable;
using DecisionDriven.Analyzers.Rules;
using Microsoft.CodeAnalysis;
using Xunit;

namespace DecisionDriven.Analyzers.Tests.Rules;

/// <summary>
/// DD0002: every <c>InternalsVisibleTo</c> target is a test assembly.
/// </summary>
public sealed class InternalsVisibleToTests
{
    [Fact]
    public void A_grant_to_a_test_assembly_is_not_reported()
    {
        Assert.Empty(Run("[assembly: System.Runtime.CompilerServices.InternalsVisibleTo(\"Sample.Layer1.Tests\")]"));
    }

    [Fact]
    public void A_grant_to_a_non_test_assembly_is_reported()
    {
        Diagnostic diagnostic = Assert.Single(
            Run("[assembly: System.Runtime.CompilerServices.InternalsVisibleTo(\"Sample.Layer2\")]"));

        Assert.Equal("DD0002", diagnostic.Id);
        Assert.Equal(DiagnosticSeverity.Error, diagnostic.Severity);
    }

    [Fact]
    public void A_signed_grant_is_matched_on_its_simple_name()
    {
        // A signed grant carries the public key after a comma. Matching the whole string against
        // the suffix would report every signed grant, including the legitimate ones.
        Assert.Empty(Run(
            "[assembly: System.Runtime.CompilerServices.InternalsVisibleTo(\"Sample.Layer1.Tests, PublicKey=00240000048000009400000006020000\")]"));

        Assert.Single(Run(
            "[assembly: System.Runtime.CompilerServices.InternalsVisibleTo(\"Sample.Layer2, PublicKey=00240000048000009400000006020000\")]"));
    }

    [Fact]
    public void A_project_with_no_grants_is_not_reported()
    {
        Assert.Empty(Run("internal sealed class Nothing { }"));
    }

    [Fact]
    public void The_diagnostic_points_at_the_attribute()
    {
        // The grant is a line somebody wrote; a project-level error would make them go looking.
        Diagnostic diagnostic = Assert.Single(
            Run("[assembly: System.Runtime.CompilerServices.InternalsVisibleTo(\"Sample.Layer2\")]"));

        Assert.NotEqual(Location.None, diagnostic.Location);
        Assert.Equal(0, diagnostic.Location.GetLineSpan().StartLinePosition.Line);
    }

    [Fact]
    public void The_message_for_the_canonical_violating_sample_is_exact()
    {
        // DiagnosticMessages.ExactMessageTested.
        Diagnostic diagnostic = Assert.Single(
            Run("[assembly: System.Runtime.CompilerServices.InternalsVisibleTo(\"Sample.Layer2\")]"));

        Assert.Equal(
            "'Sample.Layer1' grants InternalsVisibleTo to 'Sample.Layer2', which is not a test assembly. "
            + "Decide: drop the grant and use the public surface, or move the code that needs the internals "
            + "into 'Sample.Layer1' or into a '*.Tests' assembly "
            + "| mark it [DesignDecision(typeof(<Set>.<Key>), Scope = ExceptionScope.<Scope>)] "
            + "citing the accepted decision that says so. "
            + "Do not add the attribute without a decision that answers this; "
            + "if the reason is only that the code already looked like this, take the design change.",
            diagnostic.GetMessage());
    }

    private static ImmutableArray<Diagnostic> Run(string source) =>
        RuleHarness.Run(new InternalsVisibleToAnalyzer(), source, assemblyName: "Sample.Layer1");
}
