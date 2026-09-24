using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;

namespace DecisionDriven.Analyzers.Rules;

/// <summary>
/// DD0014: a wrapper around one primitive is as cheap as the primitive and compares like it.
/// </summary>
/// <remarks>
/// <c>PrimitiveFreeSurfaces.WrappersAreReadonlyStructs</c>. DD0013 asks for the wrapper; this is
/// what stops the wrapper costing more than the thing it replaced. A class allocates on every one
/// of them, a mutable struct is a value that changes behind its holder's back, and one without
/// value equality compares by reference or by nothing anybody meant.
/// </remarks>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class WrapperShapeAnalyzer : DiagnosticAnalyzer
{
    /// <inheritdoc/>
    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics { get; } =
        ImmutableArray.Create(Descriptors.WrapperShape);

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
            INamedTypeSymbol? equatable = start.Compilation.GetTypeByMetadataName("System.IEquatable`1");

            start.RegisterSymbolAction(symbol => Analyze(symbol, model, banned, equatable), SymbolKind.NamedType);
        });
    }

    private static void Analyze(
        SymbolAnalysisContext context,
        DomainModelNamespaces model,
        BannedPrimitives banned,
        INamedTypeSymbol? equatable)
    {
        INamedTypeSymbol type = (INamedTypeSymbol)context.Symbol;

        // A declared model type only. A contract is not a wrapper, and an internal helper that
        // holds an int is how code is built rather than what it says.
        if (!model.Contains(type) || Markers.Has(type, Markers.DesignDecision))
        {
            return;
        }

        if (Wrappers.Wrapped(type, banned) is not { } wrapped)
        {
            return;
        }

        string primitive = wrapped.ToDisplayString(Display.Format);
        string name = type.ToDisplayString(Display.Format);

        string? fault =
            type.TypeKind == TypeKind.Class ? $"'{name}' wraps one '{primitive}' and is a class, so every one of them allocates"
            : type.TypeKind != TypeKind.Struct ? null
            : !type.IsReadOnly ? $"'{name}' wraps one '{primitive}' and is not a readonly struct, so it is a value that can change behind its holder's back"
            : !HasValueEquality(type, equatable) ? $"'{name}' wraps one '{primitive}' and has no value equality, so two of the same value are not equal"
            : null;

        if (fault is null)
        {
            return;
        }

        string designChange = $"make it 'public readonly record struct {type.Name}({primitive} Value)', "
            + "or a readonly struct implementing IEquatable<T> with == and !=";

        foreach (Location location in type.Locations.IsEmpty ? ImmutableArray.Create(Location.None) : type.Locations)
        {
            context.ReportDiagnostic(Diagnostic.Create(Descriptors.WrapperShape, location, fault, designChange));
        }
    }

    /// <summary>
    /// Equality somebody wrote, rather than the one a struct gets for free.
    /// </summary>
    /// <remarks>
    /// The default <c>ValueType.Equals</c> exists and is not this: it compares by reflection or by
    /// bytes depending on the layout, it boxes, and nobody chose it. A record struct satisfies this
    /// by construction, which is why it is what the message suggests.
    /// </remarks>
    private static bool HasValueEquality(INamedTypeSymbol type, INamedTypeSymbol? equatable)
    {
        if (type.IsRecord)
        {
            return true;
        }

        if (equatable is not null)
        {
            INamedTypeSymbol wanted = equatable.Construct(type);

            foreach (INamedTypeSymbol implemented in type.AllInterfaces)
            {
                if (SymbolEqualityComparer.Default.Equals(implemented, wanted))
                {
                    return true;
                }
            }
        }

        foreach (ISymbol member in type.GetMembers("Equals"))
        {
            if (member is IMethodSymbol { IsOverride: true })
            {
                return true;
            }
        }

        return false;
    }
}
