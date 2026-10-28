using System.Collections.Immutable;
using System.Composition;
using System.Threading;
using System.Threading.Tasks;
using DecisionDriven.Analyzers.Rules;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CodeActions;
using Microsoft.CodeAnalysis.CodeFixes;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace DecisionDriven.Analyzers.CodeFixes;

/// <summary>
/// DD0022's design-change path, the half of it that is mechanical: declare the namespace the folder
/// names.
/// </summary>
/// <remarks>
/// <para>
/// The other half, moving the file to the folder the namespace names, is not a code fix: a code
/// action edits documents, and a move is a change to the project tree that the IDE's own rename
/// does with the references updated. A type in the global namespace gets no fix either, because
/// wrapping it in a declaration is a decision about which of the file's members belong together.
/// </para>
/// <para>
/// The new name comes from the diagnostic rather than being re-derived here, so that the fix and
/// the analyzer cannot disagree about what the folder names.
/// </para>
/// </remarks>
[ExportCodeFixProvider(LanguageNames.CSharp, Name = nameof(NamespaceFolderFix))]
[Shared]
public sealed class NamespaceFolderFix : CodeFixProvider
{
    /// <inheritdoc/>
    public override ImmutableArray<string> FixableDiagnosticIds { get; } =
        ImmutableArray.Create(DiagnosticIds.NamespaceFolder);

    /// <inheritdoc/>
    /// <remarks>
    /// No fix-all. Each rename changes what every other file that used the old name has to import,
    /// and a project full of renames applied at once is a wall of errors with nothing to read; one
    /// at a time, each shows who depended on the old name.
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
            if (!diagnostic.Location.IsInSource
                || !diagnostic.Properties.TryGetValue(DiagnosticProperties.NamespaceFolderNewName, out string? newName)
                || newName is not { Length: > 0 })
            {
                continue;
            }

            BaseNamespaceDeclarationSyntax? declaration = root
                .FindNode(diagnostic.Location.SourceSpan, getInnermostNodeForTie: true)
                .FirstAncestorOrSelf<BaseNamespaceDeclarationSyntax>();

            if (declaration is null)
            {
                continue;
            }

            context.RegisterCodeFix(
                CodeAction.Create(
                    "Declare the namespace the folder names: '" + newName + "'",
                    cancellationToken => RenameAsync(context.Document, declaration, newName, cancellationToken),
                    equivalenceKey: nameof(NamespaceFolderFix)),
                diagnostic);
        }
    }

    private static async Task<Document> RenameAsync(Document document, BaseNamespaceDeclarationSyntax declaration, string newName, CancellationToken cancellationToken)
    {
        SyntaxNode? root = await document.GetSyntaxRootAsync(cancellationToken).ConfigureAwait(false);
        if (root is null)
        {
            return document;
        }

        NameSyntax name = SyntaxFactory.ParseName(newName).WithTriviaFrom(declaration.Name);

        return document.WithSyntaxRoot(root.ReplaceNode(declaration.Name, name));
    }
}
