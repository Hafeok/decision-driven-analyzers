using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Operations;

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
/// <para>
/// The shape of the members is half of it. The other half is the bodies: a class whose members are
/// all private and readonly is as mutable as a type can be if a public method adds to the set it
/// holds. A non-private instance method on a model class that writes an instance field or property
/// of its own, or calls a member of a field whose type DD0004 judges mutable, is reported; so is one
/// that does either through a private helper of the same type, one call deep. Anything deeper is
/// out of scope, and the decision says so.
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
            start.RegisterSymbolStartAction(symbolStart => AnalyzeBodies(symbolStart, model), SymbolKind.NamedType);
        });
    }

    /// <summary>
    /// What one method of a model class does to the value it belongs to, as far as its own body says.
    /// </summary>
    private sealed class MethodFacts
    {
        internal MethodFacts(IMethodSymbol method)
        {
            Method = method;
        }

        internal IMethodSymbol Method { get; }

        /// <summary>The first mutation in the body, as the finding will say it, or null.</summary>
        internal string? Mutation { get; set; }

        /// <summary>The private instance methods of the same type the body calls on itself.</summary>
        internal List<IMethodSymbol> Helpers { get; } = new List<IMethodSymbol>();
    }

    private static void AnalyzeBodies(SymbolStartAnalysisContext context, DomainModelNamespaces model)
    {
        INamedTypeSymbol type = (INamedTypeSymbol)context.Symbol;

        // ImmutableModel.DomainModelImmutable, amended: a model class. A struct is checked by its
        // shape already: a readonly struct cannot write its own fields outside a constructor.
        if (type.TypeKind != TypeKind.Class
            || !Surfaces.IsModel(type, model)
            || Markers.Has(type, Markers.DesignDecision)
            || IsBuilder(type))
        {
            return;
        }

        ConcurrentDictionary<IMethodSymbol, MethodFacts> facts =
            new ConcurrentDictionary<IMethodSymbol, MethodFacts>(SymbolEqualityComparer.Default);

        context.RegisterOperationBlockAction(block =>
        {
            if (block.OwningSymbol is not IMethodSymbol { IsStatic: false, MethodKind: MethodKind.Ordinary } method
                || !SymbolEqualityComparer.Default.Equals(method.ContainingType, type))
            {
                return;
            }

            MethodFacts found = new MethodFacts(method);

            foreach (IOperation root in block.OperationBlocks)
            {
                foreach (IOperation operation in root.DescendantsAndSelf())
                {
                    found.Mutation ??= Mutation(operation, type);

                    if (operation is IInvocationOperation { Instance: IInstanceReferenceOperation { ReferenceKind: InstanceReferenceKind.ContainingTypeInstance }, TargetMethod: { } called }
                        && called.DeclaredAccessibility == Accessibility.Private
                        && !called.IsStatic
                        && SymbolEqualityComparer.Default.Equals(called.ContainingType, type))
                    {
                        found.Helpers.Add(called.OriginalDefinition);
                    }
                }
            }

            facts[method] = found;
        });

        context.RegisterSymbolEndAction(end =>
        {
            string owner = type.ToDisplayString(Display.Format);

            foreach (MethodFacts method in facts.Values)
            {
                if (method.Method.DeclaredAccessibility == Accessibility.Private
                    || Markers.Has(method.Method, Markers.DesignDecision))
                {
                    continue;
                }

                string? finding = method.Mutation;

                if (finding is null)
                {
                    // One level, and no further: the helper's own body, not the helpers it calls.
                    foreach (IMethodSymbol helper in method.Helpers)
                    {
                        if (facts.TryGetValue(helper, out MethodFacts? helperFacts) && helperFacts.Mutation is { } inner)
                        {
                            finding = $"calls '{helper.Name}', which {inner}";
                            break;
                        }
                    }
                }

                if (finding is null)
                {
                    continue;
                }

                end.ReportDiagnostic(Diagnostic.Create(
                    Descriptors.MutableModel,
                    Declaration(method.Method),
                    $"'{owner}.{method.Method.Name}' {finding}, so a value already handed out changes under whoever holds it",
                    "return a new value with the change applied instead of changing this one, or do the changing in a sealed *Builder and build the value from it"));
            }
        });
    }

    /// <summary>The mutation <paramref name="operation"/> makes to an instance of <paramref name="type"/>, or null.</summary>
    private static string? Mutation(IOperation operation, INamedTypeSymbol type)
    {
        IOperation? written = operation switch
        {
            IAssignmentOperation assignment => assignment.Target,
            IIncrementOrDecrementOperation step => step.Target,
            IArgumentOperation { Parameter.RefKind: RefKind.Ref or RefKind.Out } argument => argument.Value,
            _ => null,
        };

        if (written is not null)
        {
            if (OwnMember(written, type) is { } member)
            {
                return $"writes '{member.Name}'";
            }

            // A setter or indexer on a field the value holds: _counts[key] = n.
            if (written is IPropertyReferenceOperation { Instance: { } instance, Property: { } property }
                && MutableField(instance, type) is { } holder)
            {
                return CallsOnField(holder, property.IsIndexer ? "this[]" : property.Name);
            }

            return null;
        }

        if (operation is IInvocationOperation { Instance: { } target, TargetMethod: { } called }
            && called.MethodKind != MethodKind.DelegateInvoke
            && called.ContainingType?.SpecialType != SpecialType.System_Object
            && MutableField(target, type) is { } field)
        {
            return CallsOnField(field, called.Name);
        }

        return null;
    }

    private static string CallsOnField(IFieldSymbol field, string member) =>
        $"calls '{field.Name}.{member}' on the '{field.Type.ToDisplayString(Display.Format)}' it holds";

    /// <summary>An instance field or property of this type, reached through <c>this</c>.</summary>
    /// <remarks>
    /// Through <c>this</c> only, not the implicit receiver of an object initializer: in
    /// <c>new Quad { Count = n }</c> the write is to the new value, which is how an immutable type
    /// hands back a changed copy.
    /// </remarks>
    private static ISymbol? OwnMember(IOperation operation, INamedTypeSymbol type) => operation switch
    {
        IFieldReferenceOperation { Field: { IsStatic: false } field, Instance: IInstanceReferenceOperation { ReferenceKind: InstanceReferenceKind.ContainingTypeInstance } }
            when SymbolEqualityComparer.Default.Equals(field.ContainingType, type) => field,
        IPropertyReferenceOperation { Property: { IsStatic: false, IsIndexer: false } property, Instance: IInstanceReferenceOperation { ReferenceKind: InstanceReferenceKind.ContainingTypeInstance } }
            when SymbolEqualityComparer.Default.Equals(property.ContainingType, type) => property,
        _ => null,
    };

    /// <summary>An instance field of this type whose type DD0004 judges mutable.</summary>
    private static IFieldSymbol? MutableField(IOperation operation, INamedTypeSymbol type) =>
        operation is IFieldReferenceOperation { Field: { IsStatic: false } field, Instance: IInstanceReferenceOperation { ReferenceKind: InstanceReferenceKind.ContainingTypeInstance } }
            && SymbolEqualityComparer.Default.Equals(field.ContainingType, type)
            && MutableTypes.IsMutable(field.Type)
            ? field
            : null;

    private static void Analyze(SymbolAnalysisContext context, DomainModelNamespaces model)
    {
        INamedTypeSymbol type = (INamedTypeSymbol)context.Symbol;

        if (!Surfaces.IsModel(type, model)
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
