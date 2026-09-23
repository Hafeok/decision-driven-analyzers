using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;

namespace DecisionDriven.Analyzers.Rules;

/// <summary>
/// DD0004: no mutable static state.
/// </summary>
/// <remarks>
/// <c>StaticState.NoMutableStaticState</c>. Two defects in one rule: a plain mutable static is the
/// concurrency one, and a static registry is the coupling one - it is how a lower layer discovers a
/// higher one without a reference, which is the hole DD0001 would otherwise leave open.
/// </remarks>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class StaticStateAnalyzer : DiagnosticAnalyzer
{
    private const string DesignDecisionAttributeName = "DesignDecisionAttribute";
    private const string AttributeNamespace = "DecisionDriven";
    private const string ThreadStaticAttributeName = "ThreadStaticAttribute";
    private const string TestAssemblySuffix = ".Tests";

    /// <inheritdoc/>
    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics { get; } =
        ImmutableArray.Create(Descriptors.MutableStaticState);

    /// <inheritdoc/>
    public override void Initialize(AnalysisContext context)
    {
        context.EnableConcurrentExecution();
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);

        context.RegisterCompilationStartAction(static start =>
        {
            // ADR-A05 exempts test assemblies: a test fixture holding shared state is a test
            // concern, and reporting it would push people towards suppressions in the one place
            // where the pattern is defensible.
            if ((start.Compilation.AssemblyName ?? string.Empty).EndsWith(TestAssemblySuffix, System.StringComparison.Ordinal))
            {
                return;
            }

            start.RegisterSymbolAction(AnalyzeField, SymbolKind.Field);
            start.RegisterSymbolAction(AnalyzeProperty, SymbolKind.Property);
        });
    }

    private static void AnalyzeField(SymbolAnalysisContext context)
    {
        IFieldSymbol field = (IFieldSymbol)context.Symbol;

        // const is a compile-time literal: there is no storage to share.
        if (!field.IsStatic || field.IsConst || field.IsImplicitlyDeclared)
        {
            return;
        }

        if (HasDesignDecision(field))
        {
            return;
        }

        if (HasThreadStatic(field))
        {
            Report(context, field, $"static field '{Name(field)}' is [ThreadStatic]", "give each caller its own instance and pass it, rather than hiding one per thread");
            return;
        }

        if (!field.IsReadOnly)
        {
            Report(context, field, $"static field '{Name(field)}' is not readonly", "make it readonly if it never changes, or give it to the object that owns it as instance state");
            return;
        }

        CheckType(context, field, field.Type, "field");
    }

    private static void AnalyzeProperty(SymbolAnalysisContext context)
    {
        IPropertySymbol property = (IPropertySymbol)context.Symbol;

        if (!property.IsStatic || property.IsImplicitlyDeclared)
        {
            return;
        }

        if (HasDesignDecision(property))
        {
            return;
        }

        // An expression-bodied or get-only static property computes; it holds nothing. A settable
        // one is a mutable static wearing an accessor.
        if (property.SetMethod is { } setter && !setter.IsInitOnly)
        {
            Report(context, property, $"static property '{Name(property)}' has a setter", "make it get-only, or give the state to the object that owns it");
            return;
        }

        CheckType(context, property, property.Type, "property");
    }

    private static void CheckType(SymbolAnalysisContext context, ISymbol symbol, ITypeSymbol type, string kind)
    {
        if (!MutableTypes.IsMutable(type))
        {
            return;
        }

        string typeName = type.ToDisplayString();

        // A collection is the registry shape: something adds to it and something else reads it,
        // and neither one appears in the reference graph. ADR-A05 describes registry detection as
        // "a static collection written from any member"; a static collection is reported whether
        // or not a write is found, because a lookup table that nothing writes is still shared
        // mutable state and the write can always be added later.
        string finding = IsCollection(type)
            ? $"static readonly {kind} '{Name(symbol)}' holds a mutable collection '{typeName}', which is the shape of a static registry"
            : $"static readonly {kind} '{Name(symbol)}' is of mutable type '{typeName}'";

        string designChange = IsCollection(type)
            ? "make it an immutable collection if it never changes, or give the collection to the object that owns it and pass that object where it is needed"
            : $"use an immutable type for '{Name(symbol)}', or give the state to the object that owns it";

        Report(context, symbol, finding, designChange);
    }

    private static bool IsCollection(ITypeSymbol type) =>
        type is IArrayTypeSymbol
        || type.AllInterfaces.Any(static i =>
            i.OriginalDefinition.SpecialType is SpecialType.System_Collections_IEnumerable
                or SpecialType.System_Collections_Generic_IEnumerable_T);

    /// <summary>
    /// <c>StaticState.PoolsExemptByDesignDecision</c>. Any <c>[DesignDecision]</c> exempts the
    /// member; whether its argument is a real decision and its scope the right one is DD0007's
    /// question, not this rule's. One rule, one judgement.
    /// </summary>
    private static bool HasDesignDecision(ISymbol symbol) =>
        symbol.GetAttributes().Any(static attribute =>
            attribute.AttributeClass is { Name: DesignDecisionAttributeName } attributeClass
            && attributeClass.ContainingNamespace?.ToDisplayString() == AttributeNamespace);

    private static bool HasThreadStatic(ISymbol symbol) =>
        symbol.GetAttributes().Any(static attribute => attribute.AttributeClass?.Name == ThreadStaticAttributeName);

    private static string Name(ISymbol symbol) =>
        symbol.ContainingType is { } containing ? containing.Name + "." + symbol.Name : symbol.Name;

    private static void Report(SymbolAnalysisContext context, ISymbol symbol, string finding, string designChange)
    {
        Location location = symbol.Locations.FirstOrDefault(static l => l.IsInSource) ?? Location.None;
        context.ReportDiagnostic(Diagnostic.Create(Descriptors.MutableStaticState, location, finding, designChange));
    }
}
