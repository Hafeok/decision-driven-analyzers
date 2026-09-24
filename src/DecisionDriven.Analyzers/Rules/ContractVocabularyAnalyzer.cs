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

                string designChange = named.ContainingAssembly is { } assembly
                    && !SymbolEqualityComparer.Default.Equals(assembly, context.Compilation.Assembly)
                        ? $"use a type the contract may already name, or add '{assembly.Name}' to ArchContractTypeAssemblies in a decision that says why every consumer of this contract now depends on it"
                        : "move the type into a [DomainModel] namespace of this assembly, or mark the type itself [Contract(typeof(<Set>.<Key>), Role = \"...\")]";

                context.ReportDiagnostic(Diagnostic.Create(
                    Descriptors.ContractVocabulary,
                    part.Location,
                    finding,
                    designChange));
            }
        }
    }
}
