using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;

namespace DecisionDriven.Analyzers.Rules;

/// <summary>
/// DD0015: a model type does not turn back into its primitive by itself.
/// </summary>
/// <remarks>
/// <c>PrimitiveFreeSurfaces.NoImplicitPrimitiveConversions</c>. An implicit conversion puts the
/// swappable argument back while leaving the signature looking like it was fixed: the parameter says
/// <c>Position</c>, DD0013 is satisfied, and a caller may still pass a <c>long</c>. An explicit
/// operator and a named accessor both cost a word at the call site, and the word is the point.
/// </remarks>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class ImplicitConversionAnalyzer : DiagnosticAnalyzer
{
    private const string ImplicitOperator = "op_Implicit";

    /// <inheritdoc/>
    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics { get; } =
        ImmutableArray.Create(Descriptors.ImplicitPrimitiveConversion);

    /// <inheritdoc/>
    public override void Initialize(AnalysisContext context)
    {
        context.EnableConcurrentExecution();
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);

        context.RegisterCompilationStartAction(start =>
        {
            ArchOptions options = ArchOptions.Read(start.Options.AnalyzerConfigOptionsProvider.GlobalOptions);

            if (!ContractScope.Applies(start.Compilation, options))
            {
                return;
            }

            DomainModelNamespaces model = DomainModelNamespaces.Read(start.Compilation);
            BannedPrimitives banned = BannedPrimitives.Read(start.Options.AnalyzerConfigOptionsProvider.GlobalOptions);

            start.RegisterSymbolAction(symbol => Analyze(symbol, model, banned), SymbolKind.Method);
        });
    }

    private static void Analyze(SymbolAnalysisContext context, DomainModelNamespaces model, BannedPrimitives banned)
    {
        if (context.Symbol is not IMethodSymbol { MethodKind: MethodKind.Conversion, Name: ImplicitOperator } conversion
            || conversion.Parameters.Length != 1)
        {
            return;
        }

        if (conversion.ContainingType is not { } owner || !model.Contains(owner))
        {
            return;
        }

        if (Markers.Has(conversion, Markers.DesignDecision))
        {
            return;
        }

        ITypeSymbol from = conversion.Parameters[0].Type;
        ITypeSymbol to = conversion.ReturnType;

        string? direction =
            banned.IsBanned(to) ? $"to '{to.ToDisplayString(Display.Format)}'"
            : banned.IsBanned(from) ? $"from '{from.ToDisplayString(Display.Format)}'"
            : null;

        if (direction is null)
        {
            return;
        }

        string finding = $"'{owner.ToDisplayString(Display.Format)}' has an implicit conversion {direction}, "
            + "so the primitive and the model type are interchangeable again";

        string designChange = "make it 'explicit', or replace it with a named accessor such as "
            + "'.Value' - both cost a word at the call site, which is the word that says which of "
            + "the two this is";

        foreach (Location location in conversion.Locations.IsEmpty ? ImmutableArray.Create(Location.None) : conversion.Locations)
        {
            context.ReportDiagnostic(Diagnostic.Create(Descriptors.ImplicitPrimitiveConversion, location, finding, designChange));
        }
    }
}
