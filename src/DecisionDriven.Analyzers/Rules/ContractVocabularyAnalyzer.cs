using System.Collections.Generic;
using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;

namespace DecisionDriven.Analyzers.Rules;

/// <summary>
/// DD0010: a contract names only types its consumers already agreed to.
/// </summary>
/// <remarks>
/// <para>
/// <c>Contracts.ContractVocabularyAllowList</c>. Implementing a contract means taking on every type
/// in its signatures, and calling one means the same. Neither party agreed to those separately, and
/// the reference graph shows the assembly reference without showing that a contract is what put it
/// there.
/// </para>
/// <para>
/// Generic arguments and array elements are walked, because <c>Task&lt;SomethingUndecided&gt;</c>
/// exposes the undecided type exactly as plainly as returning it would.
/// </para>
/// </remarks>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class ContractVocabularyAnalyzer : DiagnosticAnalyzer
{
    /// <inheritdoc/>
    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics { get; } =
        ImmutableArray.Create(Descriptors.ContractVocabulary);

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

            start.RegisterSymbolAction(symbol => Analyze(symbol, vocabulary), SymbolKind.NamedType);
        });
    }

    private static void Analyze(SymbolAnalysisContext context, ContractVocabulary vocabulary)
    {
        INamedTypeSymbol type = (INamedTypeSymbol)context.Symbol;

        if (!Markers.Has(type, Markers.Contract))
        {
            return;
        }

        foreach (ContractSignature.Part part in ContractSignature.Parts(type, context.CancellationToken))
        {
            HashSet<string> reported = new HashSet<string>(System.StringComparer.Ordinal);

            foreach (ITypeSymbol named in ContractSignature.Mentioned(part.Type))
            {
                context.CancellationToken.ThrowIfCancellationRequested();

                if (vocabulary.Reason(named) is not { } reason)
                {
                    continue;
                }

                string display = named.ToDisplayString(Display.Format);

                // Task<Undecided> and Undecided both mention one type, and one message about it is
                // the whole finding; a second would repeat the same sentence with more brackets.
                if (!reported.Add(display))
                {
                    continue;
                }

                string finding =
                    $"the {part.Description} of contract member '{type.Name}.{part.Member}' names '{display}', which {reason}";

                (string designChange, string exception) = Paths(named, context.Compilation);

                context.ReportDiagnostic(Diagnostic.Create(
                    Descriptors.ContractVocabulary,
                    part.Location,
                    finding,
                    designChange,
                    exception));
            }
        }
    }

    /// <summary>
    /// The two answers, each naming the thing it is about.
    /// </summary>
    /// <remarks>
    /// A type of this assembly is a question about what this assembly's model is, so the design
    /// change names the namespace to declare and the exception is to make the type a contract in its
    /// own right. A type from elsewhere is a question about which packages a contract may name, so
    /// the exception names the assembly to list - and it is the exception rather than the design
    /// change because every consumer of the contract takes that dependency on.
    /// </remarks>
    private static (string DesignChange, string Exception) Paths(ITypeSymbol type, Compilation compilation)
    {
        if (type.ContainingAssembly is not { } assembly)
        {
            return (
                "use a type this contract may already name",
                "mark the type itself [Contract(typeof(<Set>.<Key>), Role = \"...\")]");
        }

        if (SymbolEqualityComparer.Default.Equals(assembly, compilation.Assembly))
        {
            string @namespace = type.ContainingNamespace is { IsGlobalNamespace: false } containing
                ? containing.ToDisplayString()
                : string.Empty;

            string declare = @namespace.Length > 0
                ? $"declare the namespace as model with [assembly: DomainModel(\"{@namespace}\", typeof(<Set>.<Key>))], or move the type into a namespace already declared"
                : "put the type in a namespace and declare that namespace as model with [assembly: DomainModel(\"<namespace>\", typeof(<Set>.<Key>))]";

            return (
                declare,
                $"mark '{type.ToDisplayString(Display.Format)}' itself [Contract(typeof(<Set>.<Key>), Role = \"...\")]");
        }

        return (
            "use a type this contract may already name, or take what it needs into this assembly's [DomainModel] namespaces",
            $"add '{assembly.Name}' to ArchContractTypeAssemblies, in a decision that says why every consumer of this contract now depends on '{assembly.Name}'");
    }
}

