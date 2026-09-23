using System;
using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;

namespace DecisionDriven.Analyzers.Rules;

/// <summary>
/// DD0002: every <c>InternalsVisibleTo</c> target is a test assembly.
/// </summary>
/// <remarks>
/// <c>StableDependencyRules.InternalsVisibleToTestsOnly</c>. A grant to a non-test assembly is a
/// dependency that no reference graph shows and no layering rule can see: DD0001 reads references,
/// and this is not one. Tests are the single case where reaching inside is the point.
/// </remarks>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class InternalsVisibleToAnalyzer : DiagnosticAnalyzer
{
    private const string AttributeMetadataName = "System.Runtime.CompilerServices.InternalsVisibleToAttribute";
    private const string TestAssemblySuffix = ".Tests";

    /// <inheritdoc/>
    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics { get; } =
        ImmutableArray.Create(Descriptors.InternalsVisibleTo);

    /// <inheritdoc/>
    public override void Initialize(AnalysisContext context)
    {
        context.EnableConcurrentExecution();
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);

        context.RegisterCompilationAction(Analyze);
    }

    private static void Analyze(CompilationAnalysisContext context)
    {
        INamedTypeSymbol? attributeType = context.Compilation.GetTypeByMetadataName(AttributeMetadataName);
        if (attributeType is null)
        {
            return;
        }

        string assemblyName = context.Compilation.AssemblyName ?? string.Empty;

        foreach (AttributeData attribute in context.Compilation.Assembly.GetAttributes())
        {
            context.CancellationToken.ThrowIfCancellationRequested();

            if (!SymbolEqualityComparer.Default.Equals(attribute.AttributeClass, attributeType))
            {
                continue;
            }

            if (attribute.ConstructorArguments.Length != 1
                || attribute.ConstructorArguments[0].Value is not string target)
            {
                continue;
            }

            string simpleName = SimpleAssemblyName(target);

            if (simpleName.EndsWith(TestAssemblySuffix, StringComparison.Ordinal))
            {
                continue;
            }

            // The attribute's own syntax where it is in source; a grant that arrives from a
            // generated file or another compilation still gets reported, just without a caret.
            Location location = attribute.ApplicationSyntaxReference is { } reference
                ? Location.Create(reference.SyntaxTree, reference.Span)
                : Location.None;

            string finding = $"'{assemblyName}' grants InternalsVisibleTo to '{simpleName}', which is not a test assembly";
            string designChange = $"drop the grant and use the public surface, or move the code that needs the internals into '{assemblyName}' or into a '*{TestAssemblySuffix}' assembly";

            context.ReportDiagnostic(Diagnostic.Create(Descriptors.InternalsVisibleTo, location, finding, designChange));
        }
    }

    /// <summary>
    /// The simple name out of an <c>InternalsVisibleTo</c> argument.
    /// </summary>
    /// <remarks>
    /// A signed grant carries the public key after a comma: <c>"Foo.Tests, PublicKey=0024..."</c>.
    /// Matching the whole string against the suffix would report every signed grant, including the
    /// legitimate ones.
    /// </remarks>
    private static string SimpleAssemblyName(string target)
    {
        int comma = target.IndexOf(',');
        return (comma < 0 ? target : target.Substring(0, comma)).Trim();
    }
}
