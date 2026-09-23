using System.Collections.Immutable;
using System.Composition;
using System.Threading;
using System.Threading.Tasks;
using DecisionDriven.Analyzers.Rules;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CodeActions;
using Microsoft.CodeAnalysis.CodeFixes;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace DecisionDriven.Analyzers.CodeFixes;

/// <summary>
/// DD0002's design-change path: drop the grant.
/// </summary>
/// <remarks>
/// <para>
/// ADR-A14 offers a code fix for the design-change path "where it is mechanical". Of the three
/// stable-dependency rules this is the only one where it is. DD0001's design change is moving code
/// between projects or inverting a dependency, and DD0003's is threading a constructor parameter
/// through; neither is a transformation of one file, and a fix that pretended otherwise would
/// produce something that compiles and is wrong.
/// </para>
/// <para>
/// Removing the grant can break the code that relied on it, which is the point: the breakage is the
/// design change becoming visible, at compile time, in the places that were reaching inside.
/// </para>
/// </remarks>
[ExportCodeFixProvider(LanguageNames.CSharp, Name = nameof(RemoveInternalsVisibleToFix))]
[Shared]
public sealed class RemoveInternalsVisibleToFix : CodeFixProvider
{
    private const string Title = "Remove the InternalsVisibleTo grant";

    /// <inheritdoc/>
    public override ImmutableArray<string> FixableDiagnosticIds { get; } =
        ImmutableArray.Create(DiagnosticIds.InternalsVisibleTo);

    /// <inheritdoc/>
    /// <remarks>
    /// No fix-all. Removing every grant in one action would break the code that relied on them all
    /// at once, with nothing to read but a wall of errors; one at a time, each removal shows which
    /// code was reaching inside and why.
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
            if (!diagnostic.Location.IsInSource)
            {
                continue;
            }

            // The diagnostic points at the attribute, not at the list that holds it: the analyzer
            // reports through AttributeData.ApplicationSyntaxReference, which is the AttributeSyntax.
            AttributeSyntax? grant = root
                .FindNode(diagnostic.Location.SourceSpan, getInnermostNodeForTie: true)
                .FirstAncestorOrSelf<AttributeSyntax>();

            if (grant is null)
            {
                continue;
            }

            context.RegisterCodeFix(
                CodeAction.Create(
                    Title,
                    cancellationToken => RemoveAsync(context.Document, grant, cancellationToken),
                    equivalenceKey: nameof(RemoveInternalsVisibleToFix)),
                diagnostic);
        }
    }

    private static async Task<Document> RemoveAsync(Document document, AttributeSyntax grant, CancellationToken cancellationToken)
    {
        SyntaxNode? root = await document.GetSyntaxRootAsync(cancellationToken).ConfigureAwait(false);
        if (root is null)
        {
            return document;
        }

        // A list can hold several attributes, and one of them may be a legitimate grant to a test
        // assembly. Taking the whole list would remove that too, so the list goes only when the
        // reported attribute is the only thing in it.
        SyntaxNode? updated = grant.Parent is AttributeListSyntax list && list.Attributes.Count == 1
            ? root.RemoveNode(list, SyntaxRemoveOptions.KeepNoTrivia)
            : root.RemoveNode(grant, SyntaxRemoveOptions.KeepNoTrivia);

        return updated is null ? document : document.WithSyntaxRoot(updated);
    }
}
