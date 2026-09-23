using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;

namespace DecisionDriven.Analyzers.Rules;

/// <summary>
/// DD0005: no grab-bag names on an assembly or a namespace.
/// </summary>
/// <remarks>
/// <para>
/// <c>NamesAndNamespaces.BannedGrabBagNames</c>. <c>Common</c>, <c>Core</c>, <c>Utils</c> and their
/// relatives are where the second reason to change accumulates. ADR-A06's argument for making this
/// a name rule rather than a metric is that the metrics are worse: LCOM-family numbers are noisy,
/// penalise span-based code by construction, and are the first thing anybody suppresses.
/// </para>
/// <para>
/// <c>Extensions</c> is banned as a namespace segment and not as a type name: a
/// <c>StringExtensions</c> class says what it extends, a <c>Extensions</c> namespace says only that
/// somebody had nowhere else to put things.
/// </para>
/// </remarks>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class BannedNameAnalyzer : DiagnosticAnalyzer
{
    /// <summary>The <c>.editorconfig</c> option that replaces the default list.</summary>
    internal const string OptionName = "dd_banned_names";

    private const string InternalSegment = "Internal";

    /// <summary>The list from ADR-A06.</summary>
    internal static readonly ImmutableArray<string> DefaultBannedNames = ImmutableArray.Create(
        "Common", "Core", "Utils", "Utilities", "Helpers", "Abstractions", "Shared", "Misc", "Internal", "Extensions");

    /// <inheritdoc/>
    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics { get; } =
        ImmutableArray.Create(Descriptors.BannedName);

    /// <inheritdoc/>
    public override void Initialize(AnalysisContext context)
    {
        context.EnableConcurrentExecution();
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);

        context.RegisterCompilationStartAction(static start =>
        {
            ImmutableHashSet<string> banned = ReadBannedNames(start.Options.AnalyzerConfigOptionsProvider.GlobalOptions);

            if (banned.IsEmpty)
            {
                return;
            }

            // The assembly name, once.
            start.RegisterCompilationEndAction(end =>
            {
                string assemblyName = end.Compilation.AssemblyName ?? string.Empty;

                foreach (string segment in assemblyName.Split('.'))
                {
                    if (banned.Contains(segment))
                    {
                        end.ReportDiagnostic(Diagnostic.Create(
                            Descriptors.BannedName,
                            Location.None,
                            $"assembly '{assemblyName}' has a segment named '{segment}'",
                            $"name the assembly for what it is responsible for, or split the part that does not belong out of it"));
                    }
                }
            });

            // Namespaces are reached through the types in them: a namespace with nothing public in
            // it is not a package surface and is nobody's grab bag.
            start.RegisterSymbolAction(
                symbolContext => AnalyzeType(symbolContext, banned),
                SymbolKind.NamedType);
        });
    }

    private static void AnalyzeType(SymbolAnalysisContext context, ImmutableHashSet<string> banned)
    {
        INamedTypeSymbol type = (INamedTypeSymbol)context.Symbol;

        // Only a public surface is a package's shape. An internal helper namespace is an
        // implementation detail, which is exactly what 'Internal' is allowed to say.
        if (type.DeclaredAccessibility != Accessibility.Public || type.ContainingType is not null)
        {
            return;
        }

        INamespaceSymbol? @namespace = type.ContainingNamespace;
        if (@namespace is null || @namespace.IsGlobalNamespace)
        {
            return;
        }

        string fullNamespace = @namespace.ToDisplayString();

        for (INamespaceSymbol? current = @namespace; current is { IsGlobalNamespace: false }; current = current.ContainingNamespace)
        {
            string segment = current.Name;

            if (!banned.Contains(segment))
            {
                continue;
            }

            string designChange = segment == InternalSegment
                ? "make the types in it internal, or move them into a namespace named for what they are"
                : $"name the namespace for what the types in it are responsible for, or move them into the namespace of the thing they belong to";

            context.ReportDiagnostic(Diagnostic.Create(
                Descriptors.BannedName,
                type.Locations.Length > 0 ? type.Locations[0] : Location.None,
                $"public type '{type.Name}' is in namespace '{fullNamespace}', whose segment '{segment}' is a grab-bag name",
                designChange));

            // One finding per type is enough; a namespace with two banned segments has one problem.
            return;
        }
    }

    private static ImmutableHashSet<string> ReadBannedNames(AnalyzerConfigOptions options)
    {
        if (!options.TryGetValue(OptionName, out string? configured) || configured is not { Length: > 0 })
        {
            return DefaultBannedNames.ToImmutableHashSet(StringComparer.Ordinal);
        }

        HashSet<string> names = new HashSet<string>(StringComparer.Ordinal);

        foreach (string name in configured.Split(','))
        {
            string trimmed = name.Trim();
            if (trimmed.Length > 0)
            {
                names.Add(trimmed);
            }
        }

        return names.ToImmutableHashSet(StringComparer.Ordinal);
    }
}
