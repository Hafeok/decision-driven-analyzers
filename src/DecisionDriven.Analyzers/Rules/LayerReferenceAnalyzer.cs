using System;
using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;

namespace DecisionDriven.Analyzers.Rules;

/// <summary>
/// DD0001: a reference within a family points strictly downward.
/// </summary>
/// <remarks>
/// <para>
/// <c>StableDependencyRules.LayerReferenceStrictlyDownward</c>. A project with
/// <c>ArchFamily=F</c> and <c>ArchLayer=n</c> may reference an assembly whose name starts with
/// <c>F.</c> only if that assembly declares a layer below <c>n</c>. A family reference with no
/// declared layer is an error too: an assembly nobody placed cannot be checked, and letting it
/// through would make the rule depend on whether somebody remembered to set a property.
/// </para>
/// <para>
/// <c>StableDependencyRules.LayerDeclaredInAssemblyMetadata</c> is what makes this work across a
/// package reference and not only a project reference: the layer is read from the assembly-level
/// attribute the generator emits, which is in metadata, rather than from a property that only the
/// project being compiled can see.
/// </para>
/// </remarks>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class LayerReferenceAnalyzer : DiagnosticAnalyzer
{
    private const string ArchLayerAttributeName = "ArchLayerAttribute";
    private const string AttributeNamespace = "DecisionDriven";
    private const string TestAssemblySuffix = ".Tests";

    /// <inheritdoc/>
    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics { get; } =
        ImmutableArray.Create(Descriptors.LayerReference);

    /// <inheritdoc/>
    public override void Initialize(AnalysisContext context)
    {
        context.EnableConcurrentExecution();
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);

        // A whole-compilation question: there is no syntax that "is" a reference, so this runs once
        // per compilation rather than per node.
        context.RegisterCompilationAction(Analyze);
    }

    private static void Analyze(CompilationAnalysisContext context)
    {
        ArchOptions options = ArchOptions.Read(context.Options.AnalyzerConfigOptionsProvider.GlobalOptions);

        // Unset means the project has not opted into layering. Reporting here would make adopting
        // the package a build break for every project that has not been placed yet.
        if (options.Family is not { Length: > 0 } family || options.Layer is not int layer)
        {
            return;
        }

        string assemblyName = context.Compilation.AssemblyName ?? string.Empty;

        // Test assemblies are exempt from being checked. A test project reaches across layers on
        // purpose; it still counts as a reference when something else references it.
        if (assemblyName.EndsWith(TestAssemblySuffix, StringComparison.Ordinal))
        {
            return;
        }

        string familyPrefix = family + ".";

        foreach (IAssemblySymbol reference in context.Compilation.SourceModule.ReferencedAssemblySymbols)
        {
            context.CancellationToken.ThrowIfCancellationRequested();

            string referenceName = reference.Name;

            // Another family, or the BCL. Not this rule's business.
            if (!referenceName.StartsWith(familyPrefix, StringComparison.Ordinal))
            {
                continue;
            }

            int? referencedLayer = ReadLayer(reference);

            if (referencedLayer is null)
            {
                Report(
                    context,
                    finding: $"'{assemblyName}' is at layer {layer} and references '{referenceName}', which is in the same family and declares no layer",
                    designChange: $"set ArchLayer on '{referenceName}' to the layer it belongs at");
                continue;
            }

            if (referencedLayer.Value >= layer)
            {
                Report(
                    context,
                    finding: $"'{assemblyName}' is at layer {layer} and references '{referenceName}', which is at layer {referencedLayer.Value}",
                    designChange: $"move what '{assemblyName}' needs down to a layer below {layer}, or invert the dependency so '{referenceName}' does not have to be referenced from layer {layer}");
            }
        }
    }

    /// <summary>
    /// Reads <c>[ArchLayer(n)]</c> off a referenced assembly.
    /// </summary>
    /// <remarks>
    /// Matched by name rather than by symbol identity. Every assembly has its own internal copy of
    /// the attribute (<c>DecisionsAsTypes.AttributesAreSourceGenerated</c>), so the attribute class
    /// on a referenced assembly is never the same symbol as the one in this compilation.
    /// </remarks>
    private static int? ReadLayer(IAssemblySymbol assembly)
    {
        foreach (AttributeData attribute in assembly.GetAttributes())
        {
            INamedTypeSymbol? attributeClass = attribute.AttributeClass;

            if (attributeClass is null
                || attributeClass.Name != ArchLayerAttributeName
                || attributeClass.ContainingNamespace?.ToDisplayString() != AttributeNamespace)
            {
                continue;
            }

            if (attribute.ConstructorArguments.Length == 1
                && attribute.ConstructorArguments[0].Value is int layer)
            {
                return layer;
            }
        }

        return null;
    }

    private static void Report(CompilationAnalysisContext context, string finding, string designChange) =>
        context.ReportDiagnostic(Diagnostic.Create(Descriptors.LayerReference, Location.None, finding, designChange));
}
