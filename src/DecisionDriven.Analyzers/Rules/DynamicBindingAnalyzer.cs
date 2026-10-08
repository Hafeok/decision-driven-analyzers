using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Operations;

namespace DecisionDriven.Analyzers.Rules;

/// <summary>
/// DD0020: no <c>dynamic</c> in a layered project.
/// </summary>
/// <remarks>
/// <para>
/// <c>StableDependencyRules.NoDynamicInLayeredProjects</c>. A dynamically bound call reaches whatever
/// the runtime object happens to be, which is the loophole DD0001 closes for references and DD0011
/// for <c>object</c>-typed contract parameters. <c>CallbackLoopholeNeedsNoRule</c> said banned
/// symbols closed it; they cannot, because the compiler lowers a dynamic operation to binder call
/// sites the source never names, and a banned-symbol analyzer sees only what the source names.
/// </para>
/// <para>
/// Reported once per syntax node: the <c>dynamic</c> keyword wherever it types something, and the
/// outermost dynamically bound operation of an expression, so <c>a.B.C()</c> is one finding and not
/// three.
/// </para>
/// </remarks>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class DynamicBindingAnalyzer : DiagnosticAnalyzer
{
    private const string DynamicKeyword = "dynamic";
    private const int ShownLength = 60;

    /// <inheritdoc/>
    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics { get; } =
        ImmutableArray.Create(Descriptors.DynamicBinding);

    /// <inheritdoc/>
    public override void Initialize(AnalysisContext context)
    {
        context.EnableConcurrentExecution();
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);

        context.RegisterCompilationStartAction(static start =>
        {
            // Any project with ArchLayer set, and only those: a project that has not declared a
            // layer has not opted into the layering this rule protects.
            ArchOptions options = ArchOptions.Read(start.Options.AnalyzerConfigOptionsProvider.GlobalOptions);

            if (options.Layer is null)
            {
                return;
            }

            start.RegisterSyntaxNodeAction(AnalyzeKeyword, SyntaxKind.IdentifierName);
            start.RegisterOperationAction(
                AnalyzeOperation,
                OperationKind.DynamicInvocation,
                OperationKind.DynamicMemberReference,
                OperationKind.DynamicIndexerAccess,
                OperationKind.DynamicObjectCreation);
        });
    }

    private static void AnalyzeKeyword(SyntaxNodeAnalysisContext context)
    {
        IdentifierNameSyntax name = (IdentifierNameSyntax)context.Node;

        if (name.Identifier.ValueText != DynamicKeyword
            || context.SemanticModel.GetTypeInfo(name, context.CancellationToken).Type is not { TypeKind: TypeKind.Dynamic }
            || Answered(context.ContainingSymbol))
        {
            return;
        }

        context.ReportDiagnostic(Diagnostic.Create(
            Descriptors.DynamicBinding,
            name.GetLocation(),
            $"'{Owner(context.ContainingSymbol)}' uses 'dynamic' as a type, so what a value of it can do is decided at run time",
            DesignChange));
    }

    private static void AnalyzeOperation(OperationAnalysisContext context)
    {
        // The outermost dynamic operation only: x.Handle(m) is a member reference inside an
        // invocation, and the line has one problem.
        if (context.Operation.Parent?.Kind is OperationKind.DynamicInvocation
                or OperationKind.DynamicMemberReference
                or OperationKind.DynamicIndexerAccess
                or OperationKind.DynamicObjectCreation
            || Answered(context.ContainingSymbol))
        {
            return;
        }

        context.ReportDiagnostic(Diagnostic.Create(
            Descriptors.DynamicBinding,
            context.Operation.Syntax.GetLocation(),
            $"'{Shown(context.Operation.Syntax)}' in '{Owner(context.ContainingSymbol)}' is bound at run time, so it reaches whatever the runtime object happens to be",
            DesignChange));
    }

    private const string DesignChange =
        "type the value as the contract it is used through, and test for that with 'is', so the compiler binds the call";

    /// <summary>A <c>[DesignDecision]</c> on the member or on any type containing it answers it.</summary>
    private static bool Answered(ISymbol? symbol)
    {
        for (ISymbol? current = symbol; current is not null and not INamespaceSymbol; current = current.ContainingSymbol)
        {
            if (Markers.Has(current, Markers.DesignDecision))
            {
                return true;
            }
        }

        return false;
    }

    private static string Owner(ISymbol? symbol) => symbol switch
    {
        INamedTypeSymbol type => type.Name,
        { ContainingType: { } type } => type.Name + "." + symbol.Name,
        { } other => other.Name,
        _ => string.Empty,
    };

    private static string Shown(SyntaxNode syntax)
    {
        string text = syntax.ToString().Replace('\r', ' ').Replace('\n', ' ');
        return text.Length <= ShownLength ? text : text.Substring(0, ShownLength) + "...";
    }
}
