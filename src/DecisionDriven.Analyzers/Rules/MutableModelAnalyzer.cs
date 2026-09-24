using System;
using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;

namespace DecisionDriven.Analyzers.Rules;

/// <summary>
/// DD0019: a value handed out by a read cannot be changed by whoever got it.
/// </summary>
/// <remarks>
/// <para>
/// <c>ImmutableModel.DomainModelImmutable</c>. A snapshot is only a snapshot if the caller cannot
/// write to it. A settable property, a mutable field or an exposed <c>List&lt;T&gt;</c> means every
/// holder of the value shares one, and the isolation the read promised was never there.
/// </para>
/// <para>
/// <c>ImmutableModel.BuildersAreTheEscapeHatch</c>: mutation while a value is being assembled is
/// fine, and a <c>*Builder</c> in the same namespace is where it goes. It has to be sealed, so that
/// the mutable thing is one type rather than a hierarchy of them.
/// </para>
/// </remarks>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class MutableModelAnalyzer : DiagnosticAnalyzer
{
    /// <summary>The suffix that makes a type the escape hatch.</summary>
    internal const string BuilderSuffix = "Builder";

    private static readonly string[] MutableCollections =
    {
        "System.Collections.Generic.List",
        "System.Collections.Generic.Dictionary",
        "System.Collections.Generic.HashSet",
        "System.Collections.Generic.SortedSet",
        "System.Collections.Generic.SortedDictionary",
        "System.Collections.Generic.Queue",
        "System.Collections.Generic.Stack",
        "System.Collections.Generic.ICollection",
        "System.Collections.Generic.IList",
        "System.Collections.Generic.ISet",
        "System.Collections.Generic.IDictionary",
        "System.Collections.ObjectModel.Collection",
        "System.Collections.IList",
        "System.Collections.IDictionary",
    };

    /// <inheritdoc/>
    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics { get; } =
        ImmutableArray.Create(Descriptors.MutableModel);

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

        if (!model.Contains(type)
            || type.DeclaredAccessibility != Accessibility.Public
            || Markers.Has(type, Markers.DesignDecision))
        {
            return;
        }

        if (IsBuilder(type))
        {
            if (!type.IsSealed)
            {
                Report(
                    context,
                    Declaration(type),
                    $"'{type.ToDisplayString(Display.Format)}' is a builder and is not sealed, so the "
                        + "mutable type is a hierarchy rather than one type",
                    "seal it");
            }

            // Everything else about a builder is allowed. Assembling a value is what it is for.
            return;
        }

        if (type.TypeKind == TypeKind.Struct && !type.IsReadOnly)
        {
            Report(
                context,
                Declaration(type),
                $"'{type.ToDisplayString(Display.Format)}' is a model struct and is not readonly",
                "make it a 'readonly record struct', or a readonly struct");
        }

        foreach (ISymbol member in type.GetMembers())
        {
            context.CancellationToken.ThrowIfCancellationRequested();

            if (member.IsImplicitlyDeclared || member.DeclaredAccessibility is Accessibility.Private)
            {
                continue;
            }

            switch (member)
            {
                case IPropertySymbol property:
                    Check(context, type, property, property.Type, property.SetMethod);
                    break;

                case IFieldSymbol { IsConst: false, IsStatic: false } field:
                    Check(context, type, field, field.Type, null, field.IsReadOnly);
                    break;
            }
        }
    }

    private static void Check(
        SymbolAnalysisContext context,
        INamedTypeSymbol type,
        ISymbol member,
        ITypeSymbol memberType,
        IMethodSymbol? setter,
        bool readOnlyField = true)
    {
        string owner = type.ToDisplayString(Display.Format);

        // init is a setter the caller can only reach while the value is being made, which is the
        // whole distinction this rule is drawing.
        if (setter is { IsInitOnly: false })
        {
            string visibility = setter.DeclaredAccessibility == Accessibility.Public ? "a public" : "an internal";

            Report(
                context,
                Declaration(member),
                $"'{owner}.{member.Name}' has {visibility} setter",
                "make it 'init', or take the setter off and set the value in the constructor");
            return;
        }

        if (!readOnlyField)
        {
            Report(
                context,
                Declaration(member),
                $"'{owner}.{member.Name}' is a field that is not readonly",
                "make it readonly, or make it an init-only property");
            return;
        }

        if (IsMutableCollection(memberType))
        {
            Report(
                context,
                Declaration(member),
                $"'{owner}.{member.Name}' is '{memberType.ToDisplayString(Display.Format)}', which the "
                    + "caller can add to and remove from",
                "expose ImmutableArray<T>, IReadOnlyList<T>, ReadOnlyMemory<T> or a frozen "
                    + "collection - the value is handed out, so it is read-only from here on");
        }
    }

    /// <summary>
    /// A collection the holder can write to, including an array.
    /// </summary>
    /// <remarks>
    /// An array is the one that gets missed: <c>readonly T[] Items</c> is a readonly reference to a
    /// thoroughly writable thing, and the <c>readonly</c> makes it look answered.
    /// </remarks>
    private static bool IsMutableCollection(ITypeSymbol type)
    {
        if (type is IArrayTypeSymbol)
        {
            return true;
        }

        if (type is not INamedTypeSymbol named)
        {
            return false;
        }

        string name = Wrappers.Name(named);

        foreach (string candidate in MutableCollections)
        {
            if (string.Equals(name, candidate, StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>True for the escape hatch: a type whose name ends in Builder.</summary>
    internal static bool IsBuilder(ITypeSymbol type) =>
        type.Name.EndsWith(BuilderSuffix, StringComparison.Ordinal) && type.Name.Length > BuilderSuffix.Length;

    private static Location Declaration(ISymbol symbol)
    {
        foreach (Location location in symbol.Locations)
        {
            if (location.IsInSource)
            {
                return location;
            }
        }

        return Location.None;
    }

    private static void Report(SymbolAnalysisContext context, Location location, string finding, string designChange) =>
        context.ReportDiagnostic(Diagnostic.Create(Descriptors.MutableModel, location, finding, designChange));
}
