using System;
using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;

namespace DecisionDriven.Analyzers.Rules;

/// <summary>
/// DD0013: a model or contract surface says what a value is, not what it is made of.
/// </summary>
/// <remarks>
/// <para>
/// <c>PrimitiveFreeSurfaces.NoNakedPrimitivesOnModelAndContract</c>. The defect is
/// <c>Read(long position, ulong graphId)</c>: the compiler will let a caller swap them, and nothing
/// anywhere says they are different things.
/// </para>
/// <para>
/// <c>PrimitiveFreeSurfaces.BoundaryMembersExempt</c> is the other half. Primitives have to come in
/// somewhere, and the place they come in is the wrapper's own members and the parse and format
/// boundary. A rule without that exemption would make the model unbuildable.
/// </para>
/// </remarks>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class NakedPrimitiveAnalyzer : DiagnosticAnalyzer
{
    private static readonly string[] BoundaryMembers = { "Parse", "TryParse", "Format", "TryFormat" };

    private static readonly string[] BoundaryInterfaces =
    {
        "System.IParsable",
        "System.ISpanParsable",
        "System.IUtf8SpanParsable",
        "System.IFormattable",
        "System.ISpanFormattable",
        "System.IUtf8SpanFormattable",
    };

    private const string HotPath = "HotPathAttribute";

    /// <inheritdoc/>
    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics { get; } =
        ImmutableArray.Create(Descriptors.NakedPrimitive);

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

            start.RegisterSymbolAction(symbol => Analyze(symbol, model, banned), SymbolKind.NamedType);
        });
    }

    private static void Analyze(SymbolAnalysisContext context, DomainModelNamespaces model, BannedPrimitives banned)
    {
        INamedTypeSymbol type = (INamedTypeSymbol)context.Symbol;

        if (!Surfaces.Applies(type, model))
        {
            return;
        }

        if (IsExempt(type))
        {
            return;
        }

        // PrimitiveFreeSurfaces.WrapperExposesItsOwnPrimitive: the wrapper's own members are where
        // its primitive is allowed to appear. Without this, Position(long) and Position.Value are
        // both errors and the type nobody can build is the one the rule was asking for. The
        // exemption is the wrapped primitive, not the type: a Guid on Position is still reported.
        ITypeSymbol? wrapped = Wrappers.Wrapped(type, banned);

        foreach (ContractSignature.Part part in Surfaces.Parts(type, context.CancellationToken))
        {
            context.CancellationToken.ThrowIfCancellationRequested();

            if (IsExempt(part.Owner) || IsBoundaryMember(part.Owner, type))
            {
                continue;
            }

            if (Naked(part.Type, banned) is not { } primitive)
            {
                continue;
            }

            if (wrapped is not null && SymbolEqualityComparer.Default.Equals(primitive, wrapped))
            {
                continue;
            }

            string written = part.Type.ToDisplayString(Display.Format);
            string name = primitive.ToDisplayString(Display.Format);

            string finding = written == name
                ? $"the {part.Description} of '{type.Name}.{part.Member}' is '{name}'"
                : $"the {part.Description} of '{type.Name}.{part.Member}' is '{written}', which is '{name}'";

            string designChange =
                $"give '{name}' a name: a readonly record struct wrapping it, so that two of them "
                + "cannot be swapped and the surface says which is which";

            context.ReportDiagnostic(Diagnostic.Create(
                Descriptors.NakedPrimitive,
                part.Location,
                finding,
                designChange));
        }
    }

    /// <summary>
    /// The banned primitive inside a written type, or null when there is none.
    /// </summary>
    /// <remarks>
    /// Spans and memories stop the walk whatever they hold: they are the shape a primitive is
    /// allowed to arrive in, which is the whole reason the parse boundary can exist. Tasks,
    /// sequences and arrays do not, because <c>Task&lt;string&gt;</c> hands back a string.
    /// </remarks>
    private static ITypeSymbol? Naked(ITypeSymbol type, BannedPrimitives banned)
    {
        switch (type)
        {
            case IArrayTypeSymbol array:
                return Naked(array.ElementType, banned);

            case INamedTypeSymbol named when Wrappers.IsBuffer(named):
                return null;

            case INamedTypeSymbol { IsGenericType: true } named when Wrappers.IsTransparent(named):
                return Naked(named.TypeArguments[0], banned);

            case ITypeParameterSymbol:
                return null;

            default:
                // The list is asked first and is the whole answer. An enum is not on it by default,
                // which is how ADR-A09 exempts enums; a consumer who adds one with
                // dd_banned_primitive_types_add means it, and a check that exempted enums ahead of
                // the list would make that line do nothing while looking like it did something.
                return banned.IsBanned(type) ? type : null;
        }
    }

    private static bool IsBoundaryMember(ISymbol member, INamedTypeSymbol type)
    {
        foreach (string name in BoundaryMembers)
        {
            if (string.Equals(member.Name, name, StringComparison.Ordinal))
            {
                return true;
            }
        }

        // A factory on the wrapper: a static member handing back the type it lives on. Named by
        // shape rather than by the word "Create", because Of, From and the type's own name are all
        // in use and none of them is more of a factory than the others.
        if (member is IMethodSymbol { IsStatic: true } factory
            && SymbolEqualityComparer.Default.Equals(factory.ReturnType, type))
        {
            return true;
        }

        if (member is IMethodSymbol { MethodKind: MethodKind.Constructor })
        {
            return true;
        }

        return Implements(member, type) || IsFrameworkSignature(member, type);
    }

    /// <summary>
    /// A member whose signature another assembly chose: an override of a member declared elsewhere,
    /// or an implementation of a member of an interface declared elsewhere.
    /// </summary>
    /// <remarks>
    /// <c>Equals(object)</c>, <c>GetHashCode()</c>, <c>ToString()</c> and
    /// <c>IComparable&lt;T&gt;.CompareTo</c> cannot take the wrapper the message offers: the
    /// signature belongs to <c>object</c> or to the interface, and DD0014 asks for the first two.
    /// An interface declared in this assembly stays checked, because there the signature was chosen
    /// here, and the interface's own members are where the rule reports it.
    /// </remarks>
    private static bool IsFrameworkSignature(ISymbol member, INamedTypeSymbol type)
    {
        IAssemblySymbol? here = type.ContainingAssembly;

        if (Root(member) is { } root && !SymbolEqualityComparer.Default.Equals(root.ContainingAssembly, here))
        {
            return true;
        }

        foreach (INamedTypeSymbol implemented in type.AllInterfaces)
        {
            if (SymbolEqualityComparer.Default.Equals(implemented.ContainingAssembly, here))
            {
                continue;
            }

            foreach (ISymbol declared in implemented.GetMembers())
            {
                if (type.FindImplementationForInterfaceMember(declared) is { } implementation
                    && SymbolEqualityComparer.Default.Equals(implementation, member))
                {
                    return true;
                }
            }
        }

        return false;
    }

    /// <summary>The member an override ultimately overrides, or null when it overrides nothing.</summary>
    private static ISymbol? Root(ISymbol member)
    {
        ISymbol? root = null;
        ISymbol? current = member;

        while (current is not null)
        {
            ISymbol? overridden = current switch
            {
                IMethodSymbol { IsOverride: true } method => method.OverriddenMethod,
                IPropertySymbol { IsOverride: true } property => property.OverriddenProperty,
                IEventSymbol { IsOverride: true } @event => @event.OverriddenEvent,
                _ => null,
            };

            if (overridden is not null)
            {
                root = overridden;
            }

            current = overridden;
        }

        return root;
    }

    /// <summary>A member implementing one of the framework's parse or format interfaces.</summary>
    private static bool Implements(ISymbol member, INamedTypeSymbol type)
    {
        foreach (INamedTypeSymbol implemented in type.AllInterfaces)
        {
            string definition = Wrappers.Name(implemented);

            bool boundary = false;
            foreach (string candidate in BoundaryInterfaces)
            {
                if (string.Equals(definition, candidate, StringComparison.Ordinal))
                {
                    boundary = true;
                    break;
                }
            }

            if (!boundary)
            {
                continue;
            }

            foreach (ISymbol declared in implemented.GetMembers())
            {
                if (type.FindImplementationForInterfaceMember(declared) is { } implementation
                    && SymbolEqualityComparer.Default.Equals(implementation, member))
                {
                    return true;
                }
            }
        }

        return false;
    }

    /// <summary>
    /// A member or type that has already answered: <c>[HotPath]</c> says the primitive is the point,
    /// <c>[DesignDecision]</c> says a decision covers it.
    /// </summary>
    private static bool IsExempt(ISymbol symbol) =>
        Markers.Has(symbol, HotPath) || Markers.Has(symbol, Markers.DesignDecision);
}
