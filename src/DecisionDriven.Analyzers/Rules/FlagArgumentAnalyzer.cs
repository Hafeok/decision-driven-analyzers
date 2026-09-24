using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;

namespace DecisionDriven.Analyzers.Rules;

/// <summary>
/// DD0016: a bool parameter is a name the call site does not get to see.
/// </summary>
/// <remarks>
/// <para>
/// <c>PrimitiveFreeSurfaces.FlagArgumentsWarning</c>, and the only tier-2 rule here
/// (<c>RuleTiers.ThreeTiers</c>): a warning, because it reads a declaration and what it is trying to
/// see is a call site in somebody else's assembly. Its false-positive story is in
/// <c>docs/rules/DD0016.md</c> and was written before this file was.
/// </para>
/// <para>
/// Three shapes are left alone, and they are what separate a flag from a bool doing honest work: an
/// <c>out</c> or <c>ref</c> bool is an answer coming back, a delegate returning bool is a predicate,
/// and a bool return type is a member answering a yes-or-no question.
/// </para>
/// </remarks>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class FlagArgumentAnalyzer : DiagnosticAnalyzer
{
    /// <inheritdoc/>
    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics { get; } =
        ImmutableArray.Create(Descriptors.FlagArgument);

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

            start.RegisterSymbolAction(symbol => Analyze(symbol, model), SymbolKind.NamedType);
        });
    }

    private static void Analyze(SymbolAnalysisContext context, DomainModelNamespaces model)
    {
        INamedTypeSymbol type = (INamedTypeSymbol)context.Symbol;

        if (!Surfaces.Applies(type, model))
        {
            return;
        }

        foreach (ContractSignature.Part part in Surfaces.Parts(type, context.CancellationToken))
        {
            context.CancellationToken.ThrowIfCancellationRequested();

            if (part.Parameter is not { } parameter
                || parameter.Type.SpecialType != SpecialType.System_Boolean)
            {
                continue;
            }

            // TryParse(s, out bool value) returns its answer through the parameter. Nothing is
            // being switched, so there is nothing to give a name to.
            if (parameter.RefKind is RefKind.Out or RefKind.Ref)
            {
                continue;
            }

            if (Markers.Has(part.Owner, Markers.DesignDecision))
            {
                continue;
            }

            string finding = $"parameter '{parameter.Name}' of '{type.Name}.{part.Member}' is a bool, "
                + $"so the call site reads '{part.Member}(x, true)'";

            string designChange = "give it an enum with two named members, or split the member in two";

            context.ReportDiagnostic(Diagnostic.Create(
                Descriptors.FlagArgument,
                part.Location,
                finding,
                designChange));
        }
    }
}
