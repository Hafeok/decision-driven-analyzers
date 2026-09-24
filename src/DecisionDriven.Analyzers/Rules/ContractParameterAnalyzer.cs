using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;

namespace DecisionDriven.Analyzers.Rules;

/// <summary>
/// DD0011: a contract parameter is the data a member works on, not somebody to work with.
/// </summary>
/// <remarks>
/// <para>
/// <c>Contracts.DataVersusCollaboratorParameters</c>. A collaborator passed per call is a
/// dependency every caller has to satisfy at every call site, and the compiler cannot place it in
/// any layer, because it has no idea what will be handed in.
/// </para>
/// <para>
/// Two findings, which are the two ways of smuggling one in. An interface or abstract class that no
/// decision declares a contract is a service parameter with a type name on it, and <c>object</c> is
/// the same move with the type name taken off.
/// </para>
/// <para>
/// A BCL interface is not a collaborator. <c>IEnumerable&lt;T&gt;</c>, <c>IReadOnlyList&lt;T&gt;</c>,
/// <c>Stream</c> and <c>PipeReader</c> are data-shaped, and DD0010 is what makes the framework part
/// of the vocabulary. The ad-hoc service parameters this rule is for are never framework types.
/// </para>
/// <para>
/// One framework type is the exception, in the other direction. A contract taking an
/// <c>IServiceProvider</c> is service location with the resolution moved to the caller, and DD0003
/// cannot see it: DD0003 reads calls, and here there is no call in this assembly to read. Anything
/// implementing <c>IServiceProvider</c> counts, since a keyed or scoped provider is the same
/// parameter with a longer name.
/// </para>
/// </remarks>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class ContractParameterAnalyzer : DiagnosticAnalyzer
{
    private const string ServiceProvider = "System.IServiceProvider";

    /// <inheritdoc/>
    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics { get; } =
        ImmutableArray.Create(Descriptors.ContractParameter);

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

            ContractVocabulary vocabulary = ContractVocabulary.Read(start.Compilation, options);
            INamedTypeSymbol? serviceProvider = start.Compilation.GetTypeByMetadataName(ServiceProvider);

            start.RegisterSymbolAction(symbol => Analyze(symbol, vocabulary, serviceProvider), SymbolKind.NamedType);
        });
    }

    private static void Analyze(SymbolAnalysisContext context, ContractVocabulary vocabulary, INamedTypeSymbol? serviceProvider)
    {
        INamedTypeSymbol type = (INamedTypeSymbol)context.Symbol;

        if (!Markers.Has(type, Markers.Contract))
        {
            return;
        }

        foreach (ContractSignature.Part part in ContractSignature.Parts(type, context.CancellationToken))
        {
            if (part.Parameter is not { } parameter)
            {
                continue;
            }

            // An array of collaborators is a collaborator with a loop around it.
            ITypeSymbol declared = Unwrap(part.Type);

            if (declared.SpecialType == SpecialType.System_Object)
            {
                Report(
                    context,
                    part,
                    $"parameter '{parameter.Name}' of contract member '{type.Name}.{part.Member}' is 'object'",
                    "give it the type of the data it actually is; if it is genuinely any type, make the member generic");
                continue;
            }

            if (IsServiceProvider(declared, serviceProvider))
            {
                Report(
                    context,
                    part,
                    $"parameter '{parameter.Name}' of contract member '{type.Name}.{part.Member}' is "
                        + $"'{declared.ToDisplayString(Display.Format)}', a service provider",
                    "name the services the member actually needs, as parameters or as constructor "
                        + "dependencies of the implementing type; a contract that takes a provider is "
                        + "service location with the resolution moved to its callers, where DD0003 "
                        + "cannot see it");
                continue;
            }

            if (!IsCollaborator(declared, vocabulary))
            {
                continue;
            }

            Report(
                context,
                part,
                $"parameter '{parameter.Name}' of contract member '{type.Name}.{part.Member}' is '{declared.ToDisplayString(Display.Format)}', "
                    + $"{(declared.TypeKind == TypeKind.Interface ? "an interface" : "an abstract class")} no decision declares a contract",
                "take it through the constructor of the implementing type, or mark "
                    + $"'{declared.ToDisplayString(Display.Format)}' [Contract(typeof(<Set>.<Key>), Role = \"...\")] so that passing it is a declared dependency");
        }
    }

    /// <summary>
    /// An interface or abstract class that is not the framework's, not a declared contract, and not
    /// a delegate.
    /// </summary>
    /// <remarks>
    /// Delegates are abstract classes underneath and are exactly the thing this rule is not about:
    /// a delegate parameter is a callback the caller supplies for this call and nothing survives it.
    /// </remarks>
    private static bool IsCollaborator(ITypeSymbol type, ContractVocabulary vocabulary)
    {
        if (type.TypeKind is TypeKind.Delegate or TypeKind.Enum or TypeKind.TypeParameter
            or TypeKind.Struct or TypeKind.Error or TypeKind.Dynamic)
        {
            return false;
        }

        bool shaped = type.TypeKind == TypeKind.Interface
            || (type.TypeKind == TypeKind.Class && type.IsAbstract);

        if (!shaped)
        {
            return false;
        }

        if (Markers.Has(type, Markers.Contract))
        {
            return false;
        }

        // The framework's interfaces are how it spells data. See the remarks on the class.
        return !vocabulary.IsFramework(type);
    }

    /// <summary>
    /// <c>IServiceProvider</c> itself, or anything implementing it.
    /// </summary>
    /// <remarks>
    /// The one framework type that is a collaborator however it is spelled. A keyed or scoped
    /// provider is the same parameter with a longer name, so the check is on the interface rather
    /// than on one type's identity.
    /// </remarks>
    private static bool IsServiceProvider(ITypeSymbol type, INamedTypeSymbol? serviceProvider)
    {
        if (serviceProvider is null)
        {
            return false;
        }

        if (SymbolEqualityComparer.Default.Equals(type, serviceProvider))
        {
            return true;
        }

        foreach (INamedTypeSymbol implemented in type.AllInterfaces)
        {
            if (SymbolEqualityComparer.Default.Equals(implemented, serviceProvider))
            {
                return true;
            }
        }

        return false;
    }

    private static ITypeSymbol Unwrap(ITypeSymbol type) =>
        type is IArrayTypeSymbol array ? Unwrap(array.ElementType) : type;

    private static void Report(SymbolAnalysisContext context, ContractSignature.Part part, string finding, string designChange) =>
        context.ReportDiagnostic(Diagnostic.Create(Descriptors.ContractParameter, part.Location, finding, designChange));
}
