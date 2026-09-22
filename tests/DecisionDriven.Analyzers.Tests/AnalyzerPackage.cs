using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Reflection;
using Microsoft.CodeAnalysis.Diagnostics;

namespace DecisionDriven.Analyzers.Tests;

/// <summary>
/// The analyzer assembly as the compiler sees it.
/// </summary>
/// <remarks>
/// The tests ask the assembly which analyzers it carries rather than naming them, so that
/// a rule added in a later session is covered by the smoke test the moment it exists,
/// without anybody remembering to add it to a list here.
/// </remarks>
internal static class AnalyzerPackage
{
    /// <summary>
    /// The assembly that ships as <c>analyzers/dotnet/cs</c> in the package.
    /// </summary>
    /// <remarks>
    /// Loaded by name rather than through <c>typeof</c>: the assembly has no types yet, and
    /// once it has them they are internal implementation of rules, not something a test
    /// should have to name in order to find the assembly they live in.
    /// </remarks>
    internal static Assembly Assembly { get; } = Assembly.Load(new AssemblyName("DecisionDriven.Analyzers"));

    /// <summary>
    /// Every diagnostic analyzer the assembly exports, the way the compiler finds them:
    /// by their <see cref="DiagnosticAnalyzerAttribute"/>, not by name.
    /// </summary>
    internal static ImmutableArray<DiagnosticAnalyzer> Analyzers { get; } = Discover();

    private static ImmutableArray<DiagnosticAnalyzer> Discover()
    {
        IEnumerable<Type> candidates = Assembly
            .GetTypes()
            .Where(static type => !type.IsAbstract)
            .Where(static type => typeof(DiagnosticAnalyzer).IsAssignableFrom(type))
            .Where(static type => type.GetCustomAttribute<DiagnosticAnalyzerAttribute>() is not null);

        return candidates
            .Select(static type => (DiagnosticAnalyzer)Activator.CreateInstance(type)!)
            .ToImmutableArray();
    }
}
