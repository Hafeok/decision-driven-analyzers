using System;
using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;

namespace DecisionDriven.Analyzers.Rules;

/// <summary>
/// DD0006: every public type lives under a root namespace equal to the assembly name.
/// </summary>
/// <remarks>
/// <c>NamesAndNamespaces.RootNamespaceEqualsAssemblyName</c>. One assembly, one root namespace, one
/// name: a public type outside it is a second package hiding inside the first, and the only way to
/// find it is to read every file.
/// </remarks>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class RootNamespaceAnalyzer : DiagnosticAnalyzer
{
    private const string TestAssemblySuffix = ".Tests";

    /// <inheritdoc/>
    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics { get; } =
        ImmutableArray.Create(Descriptors.RootNamespace);

    /// <inheritdoc/>
    public override void Initialize(AnalysisContext context)
    {
        context.EnableConcurrentExecution();
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);

        context.RegisterCompilationStartAction(static start =>
        {
            string assemblyName = start.Compilation.AssemblyName ?? string.Empty;

            if (assemblyName.Length == 0)
            {
                return;
            }

            start.RegisterSymbolAction(
                symbolContext => AnalyzeType(symbolContext, assemblyName),
                SymbolKind.NamedType);
        });
    }

    private static void AnalyzeType(SymbolAnalysisContext context, string assemblyName)
    {
        INamedTypeSymbol type = (INamedTypeSymbol)context.Symbol;

        // Nested types take their namespace from the type that contains them, so checking the
        // outer one is checking all of them.
        if (type.DeclaredAccessibility != Accessibility.Public || type.ContainingType is not null)
        {
            return;
        }

        INamespaceSymbol? @namespace = type.ContainingNamespace;

        string actual = @namespace is null || @namespace.IsGlobalNamespace
            ? string.Empty
            : @namespace.ToDisplayString();

        if (actual == assemblyName
            || actual.StartsWith(assemblyName + ".", StringComparison.Ordinal))
        {
            return;
        }

        string finding = actual.Length == 0
            ? $"public type '{type.Name}' is in the global namespace, and this assembly's root namespace is '{assemblyName}'"
            : $"public type '{type.Name}' is in namespace '{actual}', which is not under this assembly's root namespace '{assemblyName}'";

        string designChange = $"move it under '{assemblyName}', or move it to the assembly whose name matches the namespace it belongs in";

        context.ReportDiagnostic(Diagnostic.Create(
            Descriptors.RootNamespace,
            type.Locations.Length > 0 ? type.Locations[0] : Location.None,
            finding,
            designChange));
    }
}
