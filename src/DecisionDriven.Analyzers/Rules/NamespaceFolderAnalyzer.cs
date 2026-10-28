using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace DecisionDriven.Analyzers.Rules;

/// <summary>
/// DD0022: a namespace declaration names the file's folder.
/// </summary>
/// <remarks>
/// <para>
/// <c>NamesAndNamespaces.NamespaceMatchesFolder</c>. The expected namespace is the root namespace
/// (<c>RootNamespace</c> when set, otherwise the assembly name) followed by the file's folders
/// relative to the project directory, one segment per folder, compared ordinally. A reader who
/// browses the tree and a reader who follows a namespace should arrive at the same file; when they
/// do not, the structure the namespaces describe is not the structure the repository has.
/// </para>
/// <para>
/// It is this package's own rule rather than IDE0130 (ADR-A17): IDE0130 runs in a build only with
/// <c>EnforceCodeStyleInBuild</c>, which the package cannot set for a consumer without switching on
/// every IDE rule, and its severity can be lowered in <c>.editorconfig</c>, which is a suppression
/// path DD0008 cannot see.
/// </para>
/// <para>
/// Silent when <c>ProjectDir</c> is not known, because there is nothing to compare against. A file
/// outside the project directory - a linked file - belongs to another project's structure and is
/// not checked, and generated code is exempt (<c>RuleTiers.GeneratedCodeIsExempt</c>).
/// </para>
/// </remarks>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class NamespaceFolderAnalyzer : DiagnosticAnalyzer
{
    private const string ProjectDirOption = "build_property.ProjectDir";
    private const string RootNamespaceOption = "build_property.RootNamespace";

    /// <inheritdoc/>
    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics { get; } =
        ImmutableArray.Create(Descriptors.NamespaceFolder);

    /// <inheritdoc/>
    public override void Initialize(AnalysisContext context)
    {
        context.EnableConcurrentExecution();
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);

        context.RegisterCompilationStartAction(static start =>
        {
            AnalyzerConfigOptions options = start.Options.AnalyzerConfigOptionsProvider.GlobalOptions;

            if (ProjectDirectory(options) is not { } projectDirectory)
            {
                return;
            }

            string root = Get(options, RootNamespaceOption) ?? start.Compilation.AssemblyName ?? string.Empty;
            if (root.Length == 0)
            {
                return;
            }

            GeneratedCode generated = GeneratedCode.For(start.Compilation, options);

            start.RegisterSemanticModelAction(model => Analyze(model, projectDirectory, root, generated));
        });
    }

    private static void Analyze(SemanticModelAnalysisContext context, string projectDirectory, string root, GeneratedCode generated)
    {
        SyntaxTree tree = context.SemanticModel.SyntaxTree;

        if (generated.Contains(tree))
        {
            return;
        }

        string path = Normalise(tree.FilePath);

        // A linked file lives in another project's structure. The prefix is compared without
        // case, as the generated-code check does: MSBuild and the compiler can disagree about a
        // Windows drive letter's case.
        if (path.Length == 0 || !path.StartsWith(projectDirectory, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        string relative = path.Substring(projectDirectory.Length);
        string[] parts = relative.Split('/');
        CompilationUnitSyntax unit = tree.GetCompilationUnitRoot(context.CancellationToken);

        List<string> folders = new List<string>(parts.Length);
        for (int i = 0; i < parts.Length - 1; i++)
        {
            string folder = parts[i];

            if (folder.Length == 0)
            {
                continue;
            }

            if (!SyntaxFacts.IsValidIdentifier(folder))
            {
                // Once per file, at the first thing a namespace could be declared on: no namespace
                // can name this folder, so comparing against one would report the wrong problem.
                if (FirstDeclaration(unit) is { } first)
                {
                    context.ReportDiagnostic(Diagnostic.Create(
                        Descriptors.NamespaceFolder,
                        first,
                        $"'{relative}' lies in folder '{folder}', which is not a valid C# identifier, so no namespace can name it",
                        "rename the folder to an identifier, or move the file out of it"));
                }

                return;
            }

            folders.Add(folder);
        }

        string expected = folders.Count == 0 ? root : root + "." + string.Join(".", folders);

        foreach (MemberDeclarationSyntax member in unit.Members)
        {
            Check(context, member, relative, expected);
        }
    }

    private static void Check(SemanticModelAnalysisContext context, MemberDeclarationSyntax member, string relative, string expected)
    {
        switch (member)
        {
            case BaseNamespaceDeclarationSyntax declaration:
                CheckNamespace(context, declaration, relative, expected);
                break;

            case BaseTypeDeclarationSyntax type:
                CheckGlobal(context, type, type.Identifier, relative, expected);
                break;

            case DelegateDeclarationSyntax @delegate:
                CheckGlobal(context, @delegate, @delegate.Identifier, relative, expected);
                break;
        }
    }

    /// <summary>
    /// A block or file-scoped declaration, by its full name. A block that holds only nested
    /// namespace blocks is not itself reported: every type ends up in some innermost declaration,
    /// and that one is checked by its full name.
    /// </summary>
    private static void CheckNamespace(SemanticModelAnalysisContext context, BaseNamespaceDeclarationSyntax declaration, string relative, string expected)
    {
        List<MemberDeclarationSyntax> types = new List<MemberDeclarationSyntax>();

        foreach (MemberDeclarationSyntax member in declaration.Members)
        {
            switch (member)
            {
                case BaseNamespaceDeclarationSyntax nested:
                    CheckNamespace(context, nested, relative, expected);
                    break;

                case BaseTypeDeclarationSyntax or DelegateDeclarationSyntax:
                    types.Add(member);
                    break;
            }
        }

        if (types.Count == 0
            || context.SemanticModel.GetDeclaredSymbol(declaration, context.CancellationToken) is not INamespaceSymbol symbol)
        {
            return;
        }

        string actual = symbol.ToDisplayString();

        if (actual == expected || AllAnswered(context, types))
        {
            return;
        }

        ImmutableDictionary<string, string?> properties = ImmutableDictionary<string, string?>.Empty;

        // What the declaration's own name becomes. For a block nested in another, only its own
        // part changes, and only when the expected name is under the containing one. The
        // containing declaration is the syntactic parent, not the symbol's containing namespace:
        // 'namespace A.B' declares B inside A as a symbol, and is one name in the file.
        string containing = declaration.Parent is BaseNamespaceDeclarationSyntax parent
            && context.SemanticModel.GetDeclaredSymbol(parent, context.CancellationToken) is INamespaceSymbol outer
            ? outer.ToDisplayString()
            : string.Empty;
        if (containing.Length == 0)
        {
            properties = properties.Add(DiagnosticProperties.NamespaceFolderNewName, expected);
        }
        else if (expected.StartsWith(containing + ".", StringComparison.Ordinal))
        {
            properties = properties.Add(DiagnosticProperties.NamespaceFolderNewName, expected.Substring(containing.Length + 1));
        }

        context.ReportDiagnostic(Diagnostic.Create(
            Descriptors.NamespaceFolder,
            declaration.Name.GetLocation(),
            properties,
            $"namespace '{actual}' is declared in '{relative}', whose folder names '{expected}'",
            $"declare '{expected}', or move the file to the folder '{actual}' names"));
    }

    private static void CheckGlobal(SemanticModelAnalysisContext context, MemberDeclarationSyntax type, SyntaxToken identifier, string relative, string expected)
    {
        if (context.SemanticModel.GetDeclaredSymbol(type, context.CancellationToken) is { } symbol
            && Markers.Has(symbol, Markers.DesignDecision))
        {
            return;
        }

        context.ReportDiagnostic(Diagnostic.Create(
            Descriptors.NamespaceFolder,
            identifier.GetLocation(),
            $"type '{identifier.ValueText}' in '{relative}' is in the global namespace, and the file's folder names '{expected}'",
            $"declare it in '{expected}'"));
    }

    /// <summary>
    /// <c>[DesignDecision]</c> on every type declared directly in the namespace declaration answers
    /// it. On some of them, it does not: the declaration still holds a type nobody decided about.
    /// </summary>
    private static bool AllAnswered(SemanticModelAnalysisContext context, List<MemberDeclarationSyntax> types)
    {
        foreach (MemberDeclarationSyntax type in types)
        {
            if (context.SemanticModel.GetDeclaredSymbol(type, context.CancellationToken) is not { } symbol
                || !Markers.Has(symbol, Markers.DesignDecision))
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>The first namespace name or type identifier in the file, or null when it declares neither.</summary>
    private static Location? FirstDeclaration(CompilationUnitSyntax unit)
    {
        foreach (MemberDeclarationSyntax member in unit.Members)
        {
            switch (member)
            {
                case BaseNamespaceDeclarationSyntax declaration:
                    return declaration.Name.GetLocation();
                case BaseTypeDeclarationSyntax type:
                    return type.Identifier.GetLocation();
                case DelegateDeclarationSyntax @delegate:
                    return @delegate.Identifier.GetLocation();
            }
        }

        return null;
    }

    /// <summary>The project directory, normalised to forward slashes and ending in one, or null when it is not known.</summary>
    private static string? ProjectDirectory(AnalyzerConfigOptions options)
    {
        if (Get(options, ProjectDirOption) is not { } projectDir)
        {
            return null;
        }

        string path = Normalise(projectDir.Trim());
        return path.EndsWith("/", StringComparison.Ordinal) ? path : path + "/";
    }

    private static string? Get(AnalyzerConfigOptions options, string key) =>
        options.TryGetValue(key, out string? value) && value is { Length: > 0 } ? value : null;

    private static string Normalise(string path) => path.Replace('\\', '/');
}
