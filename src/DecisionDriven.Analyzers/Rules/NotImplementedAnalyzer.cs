using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace DecisionDriven.Analyzers.Rules;

/// <summary>
/// DD0018: no placeholder bodies in code that ships.
/// </summary>
/// <remarks>
/// <para>
/// <c>Hierarchies.NoNotImplementedException</c>. <c>NotImplementedException</c> is the one exception
/// type that says nothing about the domain and everything about the schedule. It compiles, it
/// passes review, and the only way to find one later is to grep.
/// </para>
/// <para>
/// There is no <c>[DesignDecision]</c> exception path, and that is the point rather than an
/// oversight: a citation on a placeholder makes the placeholder permanent and still invisible. A
/// deliberate stub is a <c>NotSupportedException</c> marked <c>[DesignDecision]</c>, which DD0012
/// reports and the report tool lists, so the stub is in the index instead of in a grep.
/// </para>
/// </remarks>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class NotImplementedAnalyzer : DiagnosticAnalyzer
{
    private const string NotImplemented = "System.NotImplementedException";

    /// <inheritdoc/>
    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics { get; } =
        ImmutableArray.Create(Descriptors.NotImplemented);

    /// <inheritdoc/>
    public override void Initialize(AnalysisContext context)
    {
        context.EnableConcurrentExecution();
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);

        context.RegisterCompilationStartAction(start =>
        {
            ArchOptions options = ArchOptions.Read(start.Options.AnalyzerConfigOptionsProvider.GlobalOptions);

            // ContractScope excludes test assemblies, which is what ADR-A10's "non-test code"
            // means: a test that asserts a member throws has to be able to write the throw.
            if (!ContractScope.Applies(start.Compilation, options))
            {
                return;
            }

            if (start.Compilation.GetTypeByMetadataName(NotImplemented) is not { } notImplemented)
            {
                return;
            }

            start.RegisterSyntaxNodeAction(
                node => Analyze(node, notImplemented),
                SyntaxKind.ThrowStatement,
                SyntaxKind.ThrowExpression,
                SyntaxKind.ObjectCreationExpression,
                SyntaxKind.ImplicitObjectCreationExpression);
        });
    }

    private static void Analyze(SyntaxNodeAnalysisContext context, INamedTypeSymbol notImplemented)
    {
        // Only the creation is reported, so "throw new NotImplementedException()" is one finding
        // rather than two. A NotImplementedException that is constructed and not thrown is still a
        // placeholder, and is still found.
        ExpressionSyntax? created = context.Node switch
        {
            ObjectCreationExpressionSyntax creation => creation,
            ImplicitObjectCreationExpressionSyntax implicitCreation => implicitCreation,
            _ => null,
        };

        if (created is null)
        {
            return;
        }

        if (context.SemanticModel.GetTypeInfo(created, context.CancellationToken).Type is not { } type
            || !SymbolEqualityComparer.Default.Equals(type, notImplemented))
        {
            return;
        }

        string where = Where(context.ContainingSymbol);

        string finding = $"{where} has a NotImplementedException in it";
        string designChange = "write the member, or take it off the type until there is something to write";

        context.ReportDiagnostic(Diagnostic.Create(
            Descriptors.NotImplemented,
            created.GetLocation(),
            finding,
            designChange));
    }

    /// <summary>
    /// What to call the thing the placeholder is in.
    /// </summary>
    /// <remarks>
    /// A lambda's containing symbol is the enclosing member, which is the right answer: the lambda
    /// has no name a reader would recognise, and the member is where they will go looking.
    /// </remarks>
    private static string Where(ISymbol? symbol)
    {
        if (symbol is null)
        {
            return "this code";
        }

        ISymbol member = symbol is IMethodSymbol { AssociatedSymbol: { } associated } ? associated : symbol;

        return member.ContainingType is { } containing
            ? $"'{containing.ToDisplayString(Display.Format)}.{member.Name}'"
            : $"'{member.Name}'";
    }
}
