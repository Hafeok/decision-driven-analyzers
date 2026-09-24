using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;

namespace DecisionDriven.Analyzers.Rules;

/// <summary>
/// DD0009: every public interface, abstract class and delegate cites a decision.
/// </summary>
/// <remarks>
/// <c>Contracts.ContractsCarryAttribute</c>. No member-count threshold, and the reason is that
/// every threshold that was tried measured the wrong thing: a three-member interface can be one
/// segregation failure and a four-member sink none. What is actually wanted is that somebody
/// decided to expose it, so that is what is asked for.
/// </remarks>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class ContractDeclarationAnalyzer : DiagnosticAnalyzer
{
    /// <inheritdoc/>
    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics { get; } =
        ImmutableArray.Create(Descriptors.ContractDeclaration);

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

            start.RegisterSymbolAction(Analyze, SymbolKind.NamedType);
        });
    }

    private static void Analyze(SymbolAnalysisContext context)
    {
        INamedTypeSymbol type = (INamedTypeSymbol)context.Symbol;

        if (!ContractScope.IsContractShaped(type) || Markers.Has(type, Markers.Contract))
        {
            return;
        }

        string kind = type.TypeKind switch
        {
            TypeKind.Interface => "interface",
            TypeKind.Delegate => "delegate",
            _ => "abstract class",
        };

        string finding = $"public {kind} '{type.ToDisplayString(Display.Format)}' is part of this assembly's surface and cites no decision";
        string designChange = "make it internal if nothing outside needs it, or mark it "
            + "[Contract(typeof(<Set>.<Key>), Role = \"...\")] citing the decision that says this is a contract";

        foreach (Location location in Declarations(type))
        {
            context.ReportDiagnostic(Diagnostic.Create(Descriptors.ContractDeclaration, location, finding, designChange));
        }
    }

    /// <summary>
    /// Where the type is declared, or nowhere.
    /// </summary>
    /// <remarks>
    /// A partial type is reported on each part. One part is where somebody will add the attribute,
    /// and a diagnostic on only the other one sends them to a file that looks fine.
    /// </remarks>
    private static ImmutableArray<Location> Declarations(INamedTypeSymbol type)
    {
        ImmutableArray<Location> locations = type.Locations;

        return locations.IsEmpty ? ImmutableArray.Create(Location.None) : locations;
    }
}
