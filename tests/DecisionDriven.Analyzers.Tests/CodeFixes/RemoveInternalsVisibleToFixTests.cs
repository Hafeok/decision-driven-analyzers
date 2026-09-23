using System;
using DecisionDriven.Analyzers.CodeFixes;
using DecisionDriven.Analyzers.Rules;
using Xunit;

namespace DecisionDriven.Analyzers.Tests.CodeFixes;

/// <summary>
/// DD0002's design-change fix: the grant goes.
/// </summary>
/// <remarks>
/// The only one of the three stable-dependency rules whose design change is mechanical. DD0001's is
/// moving code between projects; DD0003's is threading a parameter through a call chain. A fix that
/// pretended either was a one-file transformation would produce something that compiles and is
/// wrong, which is worse than offering nothing.
/// </remarks>
public sealed class RemoveInternalsVisibleToFixTests
{
    [Fact]
    public void The_grant_is_removed()
    {
        string fixedSource = CodeFixHarness.ApplySingle(
            new InternalsVisibleToAnalyzer(),
            new RemoveInternalsVisibleToFix(),
            """[assembly: System.Runtime.CompilerServices.InternalsVisibleTo("Sample.Layer2")]""");

        Assert.DoesNotContain("InternalsVisibleTo", fixedSource, StringComparison.Ordinal);
    }

    [Fact]
    public void Only_the_reported_grant_is_removed()
    {
        // A list can hold several attributes, and one of them may be a legitimate grant to a test
        // assembly. Removing the whole list would take it too.
        string fixedSource = CodeFixHarness.ApplySingle(
            new InternalsVisibleToAnalyzer(),
            new RemoveInternalsVisibleToFix(),
            """
            [assembly: System.Runtime.CompilerServices.InternalsVisibleTo("Sample.Layer1.Tests")]
            [assembly: System.Runtime.CompilerServices.InternalsVisibleTo("Sample.Layer2")]
            """);

        Assert.Contains("Sample.Layer1.Tests", fixedSource, StringComparison.Ordinal);
        Assert.DoesNotContain("Sample.Layer2", fixedSource, StringComparison.Ordinal);
    }

    [Fact]
    public void The_result_compiles()
    {
        // Unlike the placeholder fix, this one is a real design change and reaches green - which is
        // the difference ADR-A14 draws between the two paths.
        string fixedSource = CodeFixHarness.ApplySingle(
            new InternalsVisibleToAnalyzer(),
            new RemoveInternalsVisibleToFix(),
            """
            [assembly: System.Runtime.CompilerServices.InternalsVisibleTo("Sample.Layer2")]

            internal sealed class Thing { }
            """);

        Assert.Empty(CodeFixHarness.CompileErrors(fixedSource));
    }
}
