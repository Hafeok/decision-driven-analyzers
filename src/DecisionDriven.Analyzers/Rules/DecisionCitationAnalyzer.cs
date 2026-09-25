using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Threading;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace DecisionDriven.Analyzers.Rules;

/// <summary>
/// DD0007: a citation names a decision the ledger produced, and carries its required arguments.
/// </summary>
/// <remarks>
/// <para>
/// <c>DecisionsAsTypes.AttributeArgumentsMustBeGenerated</c>. Three findings, one judgement: the
/// citation either binds to a decision or it does not. A citation of a decision that does not exist
/// at all is already CS0246 and is left to the compiler - repeating it here would report the same
/// line twice and teach a reader to ignore one of them.
/// </para>
/// <para>
/// <c>Role</c> and <c>Scope</c> are properties rather than constructor parameters, because a
/// <c>Type</c> is the only argument the attribute's identity needs. That makes them optional to the
/// compiler and required only here, which is the reason this rule exists at all rather than the
/// attribute signature carrying the requirement.
/// </para>
/// </remarks>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class DecisionCitationAnalyzer : DiagnosticAnalyzer
{
    private const string ContractAttribute = "DecisionDriven.ContractAttribute";
    private const string DomainModelAttribute = "DecisionDriven.DomainModelAttribute";
    private const string HotPathAttribute = "DecisionDriven.HotPathAttribute";
    private const string DesignDecisionAttribute = "DecisionDriven.DesignDecisionAttribute";

    private const string RoleArgument = "Role";
    private const string ScopeArgument = "Scope";

    /// <inheritdoc/>
    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics { get; } =
        ImmutableArray.Create(Descriptors.DecisionCitation);

    /// <inheritdoc/>
    public override void Initialize(AnalysisContext context)
    {
        context.EnableConcurrentExecution();

        // The emitted attributes live in generated code, and so does every decision type, but a
        // citation is written by hand in ordinary source. Analyze() only ever looks at attributes,
        // and an attribute in generated code is the generator's business, not a consumer's.
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);

        context.RegisterCompilationStartAction(start =>
        {
            Citations citations = Citations.Resolve(start.Compilation);
            if (citations.None)
            {
                return;
            }

            AssemblyNamespaces namespaces = new AssemblyNamespaces(start.Compilation);
            GeneratedDecisions decisions = new GeneratedDecisions(start.Compilation);

            start.RegisterSyntaxNodeAction(
                node => Analyze(node, citations, namespaces, decisions),
                SyntaxKind.Attribute);
        });
    }

    private static void Analyze(
        SyntaxNodeAnalysisContext context,
        Citations citations,
        AssemblyNamespaces namespaces,
        GeneratedDecisions decisions)
    {
        AttributeSyntax attribute = (AttributeSyntax)context.Node;

        if (context.SemanticModel.GetSymbolInfo(attribute, context.CancellationToken).Symbol
            is not IMethodSymbol { ContainingType: { } attributeType })
        {
            return;
        }

        Citation? kind = citations.Kind(attributeType);
        if (kind is null)
        {
            return;
        }

        SeparatedSyntaxList<AttributeArgumentSyntax> arguments =
            attribute.ArgumentList?.Arguments ?? default;

        // One attribute, one report. An attribute that cites nothing real is wrong for that
        // reason; a second diagnostic about a missing Role on it would be noise, and fixing the
        // citation is what brings the rest into view.
        if (!CheckDecisionArgument(context, kind.Value, arguments, decisions))
        {
            return;
        }

        CheckRequiredNamedArgument(context, attribute, kind.Value, arguments);

        if (kind.Value == Citation.DomainModel)
        {
            CheckNamespacePrefix(context, arguments, namespaces);
        }
    }

    /// <summary>The <c>decision</c> argument is a <c>typeof</c> of an emitted decision type.</summary>
    /// <returns>False when this attribute has already been reported.</returns>
    private static bool CheckDecisionArgument(
        SyntaxNodeAnalysisContext context,
        Citation kind,
        SeparatedSyntaxList<AttributeArgumentSyntax> arguments,
        GeneratedDecisions decisions)
    {
        // DomainModel takes the namespace prefix first, so the decision is the second argument.
        int position = kind == Citation.DomainModel ? 1 : 0;

        AttributeArgumentSyntax? argument = PositionalArgument(arguments, position);
        if (argument is null)
        {
            // Too few arguments is CS7036, reported on the same line.
            return false;
        }

        if (argument.Expression is not TypeOfExpressionSyntax typeOf)
        {
            Report(
                context,
                argument.GetLocation(),
                $"the decision argument of [{Name(kind)}] is not a typeof expression",
                "pass typeof of the decision type the generator emits, not a value of any other shape");
            return false;
        }

        ITypeSymbol? cited = context.SemanticModel
            .GetTypeInfo(typeOf.Type, context.CancellationToken).Type;

        // An unresolved name is already CS0246 on this very token: the decision does not exist, and
        // the compiler has said so more precisely than this rule could.
        if (cited is null || cited.TypeKind == TypeKind.Error)
        {
            return false;
        }

        GeneratedDecisions.Verdict verdict = decisions.Classify(cited as INamedTypeSymbol);

        if (verdict == GeneratedDecisions.Verdict.Generated)
        {
            return true;
        }

        string display = cited.ToDisplayString();

        string finding = verdict == GeneratedDecisions.Verdict.HandWritten
            ? $"[{Name(kind)}] cites '{display}', which is shaped like a decision but did not come out of a generator run"
            : $"[{Name(kind)}] cites '{display}', which is not a decision type";

        Report(
            context,
            typeOf.Type.GetLocation(),
            finding,
            $"drop the [{Name(kind)}] attribute, or cite a decision under {GeneratedDecisions.LedgerNamespacePrefix}* that the generator emitted");

        return false;
    }

    /// <summary>Contract requires a non-empty Role; DesignDecision requires a Scope.</summary>
    private static void CheckRequiredNamedArgument(
        SyntaxNodeAnalysisContext context,
        AttributeSyntax attribute,
        Citation kind,
        SeparatedSyntaxList<AttributeArgumentSyntax> arguments)
    {
        string? required = kind switch
        {
            Citation.Contract => RoleArgument,
            Citation.DesignDecision => ScopeArgument,
            _ => null,
        };

        if (required is null)
        {
            return;
        }

        AttributeArgumentSyntax? named = NamedArgument(arguments, required);

        if (named is null)
        {
            Report(
                context,
                attribute.Name.GetLocation(),
                $"[{Name(kind)}] does not set {required}",
                $"set {required} = {Example(kind)}, or drop the attribute if there is nothing to say");
            return;
        }

        // An empty Role is the same omission with extra characters: the label is what a reader of
        // the report tool's output sees next to the contract, and "" tells them nothing.
        if (kind == Citation.Contract
            && context.SemanticModel.GetConstantValue(named.Expression, context.CancellationToken)
                is { HasValue: true, Value: string role }
            && role.Trim().Length == 0)
        {
            Report(
                context,
                named.GetLocation(),
                $"[{Name(kind)}] sets {RoleArgument} to an empty string",
                $"name the side of the boundary this contract is, as {Example(kind)}");
        }
    }

    /// <summary>A DomainModel prefix names a namespace this assembly declares.</summary>
    private static void CheckNamespacePrefix(
        SyntaxNodeAnalysisContext context,
        SeparatedSyntaxList<AttributeArgumentSyntax> arguments,
        AssemblyNamespaces namespaces)
    {
        AttributeArgumentSyntax? argument = PositionalArgument(arguments, 0);
        if (argument is null)
        {
            return;
        }

        if (context.SemanticModel.GetConstantValue(argument.Expression, context.CancellationToken)
            is not { HasValue: true, Value: string prefix })
        {
            return;
        }

        if (namespaces.Declares(prefix, context.CancellationToken))
        {
            return;
        }

        string assembly = context.Compilation.AssemblyName ?? "this assembly";

        Report(
            context,
            argument.GetLocation(),
            $"[DomainModel] declares '{prefix}' to be domain model, and '{assembly}' has no such namespace",
            $"name a namespace '{assembly}' declares, or move the declaration to the assembly that owns the namespace");
    }

    private static AttributeArgumentSyntax? PositionalArgument(
        SeparatedSyntaxList<AttributeArgumentSyntax> arguments,
        int position)
    {
        int seen = 0;

        foreach (AttributeArgumentSyntax argument in arguments)
        {
            if (argument.NameEquals is not null)
            {
                continue;
            }

            if (seen == position)
            {
                return argument;
            }

            seen++;
        }

        return null;
    }

    private static AttributeArgumentSyntax? NamedArgument(
        SeparatedSyntaxList<AttributeArgumentSyntax> arguments,
        string name)
    {
        foreach (AttributeArgumentSyntax argument in arguments)
        {
            if (argument.NameEquals?.Name.Identifier.ValueText == name)
            {
                return argument;
            }
        }

        return null;
    }

    private static void Report(SyntaxNodeAnalysisContext context, Location location, string finding, string designChange) =>
        context.ReportDiagnostic(Diagnostic.Create(Descriptors.DecisionCitation, location, finding, designChange));

    private static string Name(Citation kind) => kind switch
    {
        Citation.Contract => "Contract",
        Citation.DomainModel => "DomainModel",
        Citation.HotPath => "HotPath",
        _ => "DesignDecision",
    };

    private static string Example(Citation kind) => kind == Citation.Contract
        ? "\"store read side\""
        : "ExceptionScope.Boundary";

    private enum Citation
    {
        Contract,
        DomainModel,
        HotPath,
        DesignDecision,
    }

    /// <summary>The four citing attributes, as this compilation sees them.</summary>
    /// <remarks>
    /// Resolved once per compilation. The attributes are emitted <c>internal</c> into every
    /// compilation, so they are matched by full metadata name and each assembly has its own.
    /// </remarks>
    private readonly struct Citations
    {
        private readonly INamedTypeSymbol?[] types;

        private Citations(INamedTypeSymbol?[] types)
        {
            this.types = types;
        }

        internal bool None
        {
            get
            {
                foreach (INamedTypeSymbol? type in types)
                {
                    if (type is not null)
                    {
                        return false;
                    }
                }

                return true;
            }
        }

        /// <summary>In <see cref="Citation"/> order: the index is the kind.</summary>
        internal static Citations Resolve(Compilation compilation) => new Citations(new[]
        {
            compilation.GetTypeByMetadataName(ContractAttribute),
            compilation.GetTypeByMetadataName(DomainModelAttribute),
            compilation.GetTypeByMetadataName(HotPathAttribute),
            compilation.GetTypeByMetadataName(DesignDecisionAttribute),
        });

        internal Citation? Kind(INamedTypeSymbol attributeType)
        {
            for (int i = 0; i < types.Length; i++)
            {
                if (types[i] is { } type && SymbolEqualityComparer.Default.Equals(type, attributeType))
                {
                    return (Citation)i;
                }
            }

            return null;
        }
    }

    /// <summary>The namespaces the assembly under compilation declares.</summary>
    /// <remarks>
    /// Read from the assembly's own namespace tree rather than the compilation's, because the
    /// compilation's includes every referenced assembly: without that distinction, declaring another
    /// package's namespace to be this assembly's domain model would pass.
    /// </remarks>
    private sealed class AssemblyNamespaces
    {
        private readonly Compilation compilation;
        private HashSet<string>? names;

        internal AssemblyNamespaces(Compilation compilation)
        {
            this.compilation = compilation;
        }

        internal bool Declares(string prefix, CancellationToken cancellationToken)
        {
            if (prefix.Length == 0)
            {
                return false;
            }

            // Two threads racing here compute the same set twice and one wins; the cost is a
            // duplicated walk of one assembly's namespaces, and a lock on every citation is worse.
            HashSet<string> declared = names ??= Collect(cancellationToken);
            return declared.Contains(prefix);
        }

        private HashSet<string> Collect(CancellationToken cancellationToken)
        {
            HashSet<string> collected = new HashSet<string>(StringComparer.Ordinal);
            Stack<INamespaceSymbol> pending = new Stack<INamespaceSymbol>();
            pending.Push(compilation.Assembly.GlobalNamespace);

            while (pending.Count > 0)
            {
                cancellationToken.ThrowIfCancellationRequested();
                INamespaceSymbol current = pending.Pop();

                if (!current.IsGlobalNamespace)
                {
                    collected.Add(current.ToDisplayString());
                }

                foreach (INamespaceSymbol child in current.GetNamespaceMembers())
                {
                    pending.Push(child);
                }
            }

            return collected;
        }
    }
}
