using System.Collections.Immutable;
using System.Composition;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using DecisionDriven.Analyzers.Rules;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CodeActions;
using Microsoft.CodeAnalysis.CodeFixes;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Formatting;

namespace DecisionDriven.Analyzers.CodeFixes;

/// <summary>
/// The documented-exception path, offered as a fix that cannot make the build green.
/// </summary>
/// <remarks>
/// <para>
/// <c>DiagnosticMessages.PlaceholderFixDoesNotCompile</c>. The fix inserts
/// <c>[DesignDecision(typeof(____.____), Scope = ExceptionScope.____)]</c>, and the placeholder does
/// not compile. The build stays red with a single remaining error naming exactly what is missing: a
/// filed, accepted decision. There is deliberately no fix that reaches green without either a design
/// change or a real decision.
/// </para>
/// <para>
/// One provider for every DD rule rather than one per rule, because the exception path is the same
/// act everywhere. A rule whose violation has no symbol to mark - DD0001 reports on the compilation,
/// not on a declaration - simply has nowhere to put the attribute, and the fix is not offered.
/// </para>
/// </remarks>
[ExportCodeFixProvider(LanguageNames.CSharp, Name = nameof(DesignDecisionPlaceholderFix))]
[Shared]
public sealed class DesignDecisionPlaceholderFix : CodeFixProvider
{
    /// <summary>The placeholder, exactly as ADR-A14 writes it.</summary>
    internal const string Placeholder = "DesignDecision(typeof(____.____), Scope = ExceptionScope.____)";

    private const string Title = "Document this as an accepted decision (placeholder; does not compile)";

    /// <inheritdoc/>
    public override ImmutableArray<string> FixableDiagnosticIds { get; } = ImmutableArray.Create(
        DiagnosticIds.LayerReference,
        DiagnosticIds.InternalsVisibleTo,
        DiagnosticIds.ServiceLocation);

    /// <inheritdoc/>
    /// <remarks>
    /// No fix-all. Applying this to every violation at once would turn a backlog of decision
    /// questions into a file full of identical placeholders, which is the laundering the guard
    /// sentence in every message exists to prevent.
    /// </remarks>
    public override FixAllProvider? GetFixAllProvider() => null;

    /// <inheritdoc/>
    public override async Task RegisterCodeFixesAsync(CodeFixContext context)
    {
        SyntaxNode? root = await context.Document.GetSyntaxRootAsync(context.CancellationToken).ConfigureAwait(false);
        if (root is null)
        {
            return;
        }

        foreach (Diagnostic diagnostic in context.Diagnostics)
        {
            // A diagnostic reported on the compilation has no source span to walk up from: there is
            // no declaration the attribute could sit on. DD0001 is that case.
            if (!diagnostic.Location.IsInSource)
            {
                continue;
            }

            SyntaxNode node = root.FindNode(diagnostic.Location.SourceSpan, getInnermostNodeForTie: true);
            SyntaxNode? target = FindTarget(node);

            if (target is null)
            {
                continue;
            }

            context.RegisterCodeFix(
                CodeAction.Create(
                    Title,
                    cancellationToken => AddPlaceholderAsync(context.Document, target, cancellationToken),
                    equivalenceKey: nameof(DesignDecisionPlaceholderFix) + ":" + diagnostic.Id),
                diagnostic);
        }
    }

    /// <summary>
    /// The nearest thing an attribute can be written on.
    /// </summary>
    /// <remarks>
    /// The attribute goes on the member containing the violation rather than on the statement,
    /// because a statement cannot carry one and because the decision being documented is about what
    /// that member does.
    /// </remarks>
    private static SyntaxNode? FindTarget(SyntaxNode node)
    {
        for (SyntaxNode? current = node; current is not null; current = current.Parent)
        {
            switch (current)
            {
                case AttributeListSyntax attributeList:
                    // DD0002 reports on an assembly attribute; the exception is documented beside it.
                    return attributeList;
                case MemberDeclarationSyntax member:
                    return member;
                case LocalFunctionStatementSyntax localFunction:
                    return localFunction;
            }
        }

        return null;
    }

    private static async Task<Document> AddPlaceholderAsync(Document document, SyntaxNode target, CancellationToken cancellationToken)
    {
        SyntaxNode? root = await document.GetSyntaxRootAsync(cancellationToken).ConfigureAwait(false);
        if (root is null)
        {
            return document;
        }

        AttributeListSyntax placeholder = SyntaxFactory
            .AttributeList(SyntaxFactory.SingletonSeparatedList(
                SyntaxFactory.Attribute(SyntaxFactory.ParseName(Placeholder.Substring(0, Placeholder.IndexOf('(')))
                    .WithoutTrivia())
                    .WithArgumentList(ArgumentList())))
            .WithAdditionalAnnotations(Formatter.Annotation);

        SyntaxNode replacement = target switch
        {
            MemberDeclarationSyntax member => member.WithAttributeLists(member.AttributeLists.Add(WithLeadingTriviaOf(placeholder, member))),
            LocalFunctionStatementSyntax localFunction => localFunction.WithAttributeLists(localFunction.AttributeLists.Add(WithLeadingTriviaOf(placeholder, localFunction))),
            AttributeListSyntax attributeList => AddBeside(attributeList, placeholder),
            _ => target,
        };

        return document.WithSyntaxRoot(root.ReplaceNode(target, replacement));
    }

    /// <summary>
    /// <c>(typeof(____.____), Scope = ExceptionScope.____)</c>, built rather than parsed.
    /// </summary>
    /// <remarks>
    /// <c>ParseAttributeArgumentList</c> is nullable-returning, and the alternatives are a
    /// null-forgiving operator on something that must never be null or a silent fallback that would
    /// emit a placeholder with no placeholders in it. Building the nodes has neither problem.
    /// </remarks>
    private static AttributeArgumentListSyntax ArgumentList() =>
        SyntaxFactory.AttributeArgumentList(
            SyntaxFactory.SeparatedList(new[]
            {
                SyntaxFactory.AttributeArgument(SyntaxFactory.ParseExpression("typeof(____.____)")),
                SyntaxFactory.AttributeArgument(SyntaxFactory.ParseExpression("ExceptionScope.____"))
                    .WithNameEquals(SyntaxFactory.NameEquals("Scope")),
            }));

    private static AttributeListSyntax WithLeadingTriviaOf(AttributeListSyntax placeholder, SyntaxNode target) =>
        placeholder.WithLeadingTrivia(target.GetLeadingTrivia()).WithTrailingTrivia(SyntaxFactory.ElasticCarriageReturnLineFeed);

    /// <summary>
    /// For an assembly-level grant: the placeholder goes next to it, targeted at the assembly too,
    /// because that is the only thing an assembly attribute's violation belongs to.
    /// </summary>
    private static AttributeListSyntax AddBeside(AttributeListSyntax existing, AttributeListSyntax placeholder) =>
        existing.WithAttributes(existing.Attributes.Add(placeholder.Attributes[0]));
}
