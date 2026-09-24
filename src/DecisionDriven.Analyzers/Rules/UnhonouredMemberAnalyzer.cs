using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace DecisionDriven.Analyzers.Rules;

/// <summary>
/// DD0012: a type does not claim a member it does not honour.
/// </summary>
/// <remarks>
/// <para>
/// <c>Contracts.NoNotSupportedFromContractMember</c>. <c>NotSupportedException</c> out of an
/// implemented member is the concrete symptom of an interface sized for somebody else's client:
/// the promise is in the type, the refusal is in the body, and every caller has to learn which
/// implementations mean it - which is the knowledge the interface existed to remove.
/// </para>
/// <para>
/// Anywhere in the body counts, not only a member whose single statement is the throw. A refusal
/// behind an <c>if</c> is the same claim with a condition on it, and it is the harder one to find
/// by reading.
/// </para>
/// </remarks>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class UnhonouredMemberAnalyzer : DiagnosticAnalyzer
{
    private const string NotSupported = "System.NotSupportedException";

    /// <inheritdoc/>
    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics { get; } =
        ImmutableArray.Create(Descriptors.UnhonouredMember);

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

            INamedTypeSymbol? notSupported = start.Compilation.GetTypeByMetadataName(NotSupported);
            if (notSupported is null)
            {
                return;
            }

            start.RegisterSyntaxNodeAction(
                node => Analyze(node, notSupported),
                SyntaxKind.ThrowStatement,
                SyntaxKind.ThrowExpression);
        });
    }

    private static void Analyze(SyntaxNodeAnalysisContext context, INamedTypeSymbol notSupported)
    {
        ExpressionSyntax? thrown = context.Node switch
        {
            ThrowStatementSyntax statement => statement.Expression,
            ThrowExpressionSyntax expression => expression.Expression,
            _ => null,
        };

        if (thrown is null)
        {
            return;
        }

        ITypeSymbol? type = context.SemanticModel.GetTypeInfo(thrown, context.CancellationToken).Type;

        if (type is null || !SymbolEqualityComparer.Default.Equals(type, notSupported))
        {
            return;
        }

        if (context.ContainingSymbol is not IMethodSymbol method)
        {
            return;
        }

        // A property or event accessor is a method to Roslyn and a member to a reader. The claim
        // being broken is the property's, so that is what is checked and what is named.
        ISymbol member = method.AssociatedSymbol ?? method;

        if (Claim(member, method) is not { } claim)
        {
            return;
        }

        // Contracts.NoNotSupportedFromContractMember is answered by a decision like every other
        // rule here; whether that decision exists and whether its scope fits is DD0007's question.
        if (Markers.Has(member, Markers.DesignDecision) || Markers.Has(method, Markers.DesignDecision))
        {
            return;
        }

        string owner = member.ContainingType?.ToDisplayString(Display.Format) ?? "this type";

        string finding = $"'{owner}.{member.Name}' throws NotSupportedException, and {claim}";
        string designChange = "split the interface so that this type only claims what it honours, "
            + "or stop implementing the member";

        context.ReportDiagnostic(Diagnostic.Create(
            Descriptors.UnhonouredMember,
            context.Node.GetLocation(),
            finding,
            designChange));
    }

    /// <summary>
    /// What the member promised, or null when it promised nothing: an ordinary method that throws
    /// is a method that throws, and no contract said otherwise.
    /// </summary>
    private static string? Claim(ISymbol member, IMethodSymbol method)
    {
        if (method.IsOverride || member is { IsOverride: true })
        {
            return "overrides a member it does not honour";
        }

        if (!method.ExplicitInterfaceImplementations.IsEmpty)
        {
            return $"explicitly implements '{method.ExplicitInterfaceImplementations[0].ContainingType.ToDisplayString(Display.Format)}'";
        }

        if (member is IPropertySymbol { ExplicitInterfaceImplementations.IsEmpty: false } property)
        {
            return $"explicitly implements '{property.ExplicitInterfaceImplementations[0].ContainingType.ToDisplayString(Display.Format)}'";
        }

        if (member.ContainingType is not { } containing)
        {
            return null;
        }

        foreach (INamedTypeSymbol contract in containing.AllInterfaces)
        {
            foreach (ISymbol declared in contract.GetMembers())
            {
                if (declared.DeclaredAccessibility != Accessibility.Public && declared.DeclaredAccessibility != Accessibility.NotApplicable)
                {
                    continue;
                }

                ISymbol? implementation = containing.FindImplementationForInterfaceMember(declared);

                if (implementation is not null
                    && (SymbolEqualityComparer.Default.Equals(implementation, member)
                        || SymbolEqualityComparer.Default.Equals(implementation, method)))
                {
                    return $"implements '{contract.ToDisplayString(Display.Format)}.{declared.Name}'";
                }
            }
        }

        return null;
    }
}
