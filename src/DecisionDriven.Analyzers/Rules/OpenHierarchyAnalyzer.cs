using System.Collections.Generic;
using System.Collections.Immutable;
using System.Threading;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace DecisionDriven.Analyzers.Rules;

/// <summary>
/// DD0017: a switch over subtypes is a claim that the list is complete; something has to have
/// closed it.
/// </summary>
/// <remarks>
/// <para>
/// <c>Hierarchies.TypeSwitchOverOpenHierarchyWarning</c>. The usual proxy - "no type switches, use
/// polymorphism" - is wrong for closed sets: an algebra, a term model and a set of deltas are
/// closed, and the right C# for them is a sealed hierarchy with an exhaustive switch that the
/// compiler checks. The defect is the switch over a hierarchy nobody closed, which silently misses
/// the next subtype.
/// </para>
/// <para>
/// Tier 2 (<c>RuleTiers.ThreeTiers</c>): a warning, because "closed" is a judgement about types
/// that do not exist yet and the rule can only see the ones that do.
/// </para>
/// </remarks>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class OpenHierarchyAnalyzer : DiagnosticAnalyzer
{
    /// <inheritdoc/>
    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics { get; } =
        ImmutableArray.Create(Descriptors.OpenHierarchySwitch);

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

            Hierarchies hierarchies = new Hierarchies(start.Compilation);

            start.RegisterSyntaxNodeAction(
                node => Analyze(node, hierarchies),
                SyntaxKind.SwitchStatement,
                SyntaxKind.SwitchExpression);
        });
    }

    private static void Analyze(SyntaxNodeAnalysisContext context, Hierarchies hierarchies)
    {
        List<INamedTypeSymbol> tested = new List<INamedTypeSymbol>();

        foreach (PatternSyntax pattern in Patterns(context.Node))
        {
            if (TestedType(pattern, context.SemanticModel, context.CancellationToken) is { } type
                && !Contains(tested, type))
            {
                tested.Add(type);
            }
        }

        // One arm testing a type is a type test, not a claim about a set.
        if (tested.Count < 2)
        {
            return;
        }

        if (hierarchies.CommonBase(tested) is not { } common)
        {
            return;
        }

        // Hierarchies.TypeSwitchOverOpenHierarchyWarning excludes the framework's own open
        // hierarchies by default. Exception and Stream are open by design and always will be, and a
        // consumer cannot close them; a warning nobody can act on is a warning people learn to skip.
        if (Framework.Owns(common, context.Compilation))
        {
            return;
        }

        if (hierarchies.IsClosed(common, context.CancellationToken))
        {
            return;
        }

        string name = common.ToDisplayString(Display.Format);

        string finding = $"this switch tests {tested.Count} subtypes of '{name}', and nothing closes '{name}'";

        string designChange = $"close the hierarchy - give '{name}' a private or file constructor "
            + "and seal its leaves, so the compiler checks the switch is exhaustive - or replace the "
            + "switch with a virtual member on the base";

        context.ReportDiagnostic(Diagnostic.Create(
            Descriptors.OpenHierarchySwitch,
            context.Node.GetFirstToken().GetLocation(),
            finding,
            designChange));
    }

    private static IEnumerable<PatternSyntax> Patterns(SyntaxNode node)
    {
        switch (node)
        {
            case SwitchStatementSyntax statement:
                foreach (SwitchSectionSyntax section in statement.Sections)
                {
                    foreach (SwitchLabelSyntax label in section.Labels)
                    {
                        if (label is CasePatternSwitchLabelSyntax cased)
                        {
                            yield return cased.Pattern;
                        }
                    }
                }

                break;

            case SwitchExpressionSyntax expression:
                foreach (SwitchExpressionArmSyntax arm in expression.Arms)
                {
                    yield return arm.Pattern;
                }

                break;
        }
    }

    /// <summary>
    /// The type an arm tests for, or null when the arm tests something else.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A declaration pattern (<c>Add a</c>), a type pattern (<c>Add</c>) and a recursive pattern
    /// with a type (<c>Add { Left: var l }</c>) are all type tests, and all three get written.
    /// </para>
    /// <para>
    /// So is a bare name in a switch expression arm - and it does not parse as a type pattern. The
    /// parser cannot know whether <c>Add</c> is a type or a constant, so it produces a constant
    /// pattern and leaves the question to binding. Missing that case would have left this rule
    /// silent on the arm shape people actually write.
    /// </para>
    /// </remarks>
    private static INamedTypeSymbol? TestedType(PatternSyntax pattern, SemanticModel model, CancellationToken cancellationToken)
    {
        if (pattern is ConstantPatternSyntax constant)
        {
            return model.GetSymbolInfo(constant.Expression, cancellationToken).Symbol as INamedTypeSymbol;
        }

        TypeSyntax? syntax = pattern switch
        {
            DeclarationPatternSyntax declaration => declaration.Type,
            TypePatternSyntax type => type.Type,
            RecursivePatternSyntax recursive => recursive.Type,
            _ => null,
        };

        return syntax is null
            ? null
            : model.GetTypeInfo(syntax, cancellationToken).Type as INamedTypeSymbol;
    }

    private static bool Contains(List<INamedTypeSymbol> types, INamedTypeSymbol type)
    {
        foreach (INamedTypeSymbol existing in types)
        {
            if (SymbolEqualityComparer.Default.Equals(existing, type))
            {
                return true;
            }
        }

        return false;
    }
}
