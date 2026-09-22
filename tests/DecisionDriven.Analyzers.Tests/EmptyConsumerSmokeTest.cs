using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis.CSharp.Testing;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Testing;
using Xunit;

namespace DecisionDriven.Analyzers.Tests;

/// <summary>
/// The package loads into a compilation and says nothing about code it has no rule about.
/// </summary>
/// <remarks>
/// This is the test that has to keep passing for every rule that is ever added. A rule
/// that reports ordinary code is worse than no rule, because a consumer cannot tell the
/// finding from the noise, so "silent on a consumer that does nothing interesting" is the
/// floor every future diagnostic is held to.
/// </remarks>
public sealed class EmptyConsumerSmokeTest
{
    private const string EmptyConsumer = """
        namespace Consumer;

        internal sealed class Nothing
        {
        }
        """;

    [Fact]
    public async Task An_empty_consumer_compiles_with_the_analyzers_loaded_and_is_not_reported()
    {
        PackageAnalyzerTest test = new()
        {
            TestCode = EmptyConsumer,
            ReferenceAssemblies = ReferenceAssemblies.Default,
        };

        // No ExpectedDiagnostics: the harness fails the test on any diagnostic it was not
        // told to expect, so an empty list is the assertion that nothing was reported.
        await test.RunAsync(TestContext.Current.CancellationToken);
    }

    [Fact]
    public void The_analyzer_assembly_declares_no_duplicate_diagnostic_ids()
    {
        IEnumerable<string> ids = AnalyzerPackage.Analyzers
            .SelectMany(static analyzer => analyzer.SupportedDiagnostics)
            .Select(static descriptor => descriptor.Id);

        IEnumerable<string> duplicated = ids
            .GroupBy(static id => id)
            .Where(static group => group.Count() > 1)
            .Select(static group => group.Key);

        Assert.Empty(duplicated);
    }

    /// <summary>
    /// Runs whatever analyzers the package exports, asking the assembly rather than naming
    /// them, so that a rule added later is covered here the moment it exists.
    /// </summary>
    /// <remarks>
    /// Roslyn will not build an analysis pass with an empty analyzer list, and this
    /// repository ships no rules yet, so while the list is empty the harness's own
    /// <see cref="EmptyDiagnosticAnalyzer"/> stands in. It registers nothing, so a
    /// diagnostic reported by this test can only have come from the package.
    /// </remarks>
    private sealed class PackageAnalyzerTest : CSharpAnalyzerTest<EmptyDiagnosticAnalyzer, DefaultVerifier>
    {
        protected override IEnumerable<DiagnosticAnalyzer> GetDiagnosticAnalyzers()
        {
            ImmutableArray<DiagnosticAnalyzer> fromPackage = AnalyzerPackage.Analyzers;

            return fromPackage.IsEmpty
                ? new DiagnosticAnalyzer[] { new EmptyDiagnosticAnalyzer() }
                : fromPackage;
        }
    }
}
