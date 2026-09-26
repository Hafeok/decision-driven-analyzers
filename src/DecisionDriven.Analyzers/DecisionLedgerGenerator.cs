using System.Collections.Generic;
using System.Collections.Immutable;
using System.Threading;
using DecisionDriven.Analyzers.Generation;
using DecisionDriven.Analyzers.Ledger;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Text;

namespace DecisionDriven.Analyzers;

/// <summary>
/// Emits the decision types that code cites, and the marker attributes it cites them with.
/// </summary>
/// <remarks>
/// <para>
/// <c>DecisionsAsTypes.LedgerIsSourceGeneratorIsReadModel</c>: the ledger is the source and this is
/// a read model over its export. Nothing here decides anything; it makes decisions citable, so that
/// a citation is a symbol reference the compiler checks rather than a string somebody greps for.
/// </para>
/// <para>
/// Two inputs, one model. The N-Triples export is the real one
/// (<c>DecisionsAsTypes.NTriplesExportIsGeneratorInput</c>); the markdown front matter is the
/// interim one (<c>DecisionsAsTypes.InterimFrontMatterUntilExport</c>) and is deleted once the
/// ledger can export. Both are told apart by <c>DdLedger</c> metadata on the
/// <c>AdditionalFiles</c> item, not by extension.
/// </para>
/// </remarks>
[Generator(LanguageNames.CSharp)]
public sealed class DecisionLedgerGenerator : IIncrementalGenerator
{
    private const string LedgerMetadata = "build_metadata.AdditionalFiles.DdLedger";
    private const string DecisionSetKind = "decision-set";
    private const string LedgerExportKind = "ledger-export";
    private const string ArchLayerProperty = "build_property.ArchLayer";

    /// <summary>Wires the generator's inputs to its outputs.</summary>
    /// <param name="context">The initialization context Roslyn supplies.</param>
    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        context.RegisterPostInitializationOutput(static ctx =>
        {
            // Every generated type is [Embedded], so no other compilation imports it. The definition
            // is Roslyn's own, shared with any other generator that asks for it.
            ctx.AddEmbeddedAttributeDefinition();
            ctx.AddSource(GeneratedAttributes.HintName, SourceText.From(GeneratedAttributes.Text, System.Text.Encoding.UTF8));
        });

        IncrementalValuesProvider<LedgerInput> ledgerFiles = context.AdditionalTextsProvider
            .Combine(context.AnalyzerConfigOptionsProvider)
            .Select(static (pair, cancellationToken) => ReadInput(pair.Left, pair.Right, cancellationToken))
            .Where(static input => input.Kind is not null);

        IncrementalValueProvider<string?> archLayer = context.AnalyzerConfigOptionsProvider
            .Select(static (provider, _) =>
                provider.GlobalOptions.TryGetValue(ArchLayerProperty, out string? value) && value is { Length: > 0 }
                    ? value
                    : null);

        context.RegisterSourceOutput(ledgerFiles.Collect().Combine(archLayer), static (ctx, data) => Execute(ctx, data.Left, data.Right));
    }

    private static LedgerInput ReadInput(
        AdditionalText file,
        Microsoft.CodeAnalysis.Diagnostics.AnalyzerConfigOptionsProvider options,
        CancellationToken cancellationToken)
    {
        if (!options.GetOptions(file).TryGetValue(LedgerMetadata, out string? kind) || kind is not { Length: > 0 })
        {
            return default;
        }

        if (kind != DecisionSetKind && kind != LedgerExportKind)
        {
            return default;
        }

        SourceText? text = file.GetText(cancellationToken);
        return text is null ? default : new LedgerInput(file.Path, kind, text.ToString());
    }

    private static void Execute(SourceProductionContext context, ImmutableArray<LedgerInput> inputs, string? archLayer)
    {
        EmitArchLayer(context, archLayer);

        Dictionary<string, LedgerNamespace> namespaces = new Dictionary<string, LedgerNamespace>(System.StringComparer.Ordinal);

        // Sorted so that the generated source does not depend on the order MSBuild happened to hand
        // the files over: a generator whose output moves between builds defeats incrementality and
        // makes a diff of generated code unreadable.
        List<LedgerInput> ordered = new List<LedgerInput>(inputs);
        ordered.Sort(static (left, right) => System.StringComparer.Ordinal.Compare(left.Path, right.Path));

        foreach (LedgerInput input in ordered)
        {
            context.CancellationToken.ThrowIfCancellationRequested();

            if (input.Kind == LedgerExportKind)
            {
                List<int> malformed = new List<int>();
                NTriplesReader.Read(input.Text!, input.Path!, namespaces, malformed);

                foreach (int line in malformed)
                {
                    context.ReportDiagnostic(Diagnostic.Create(
                        LedgerDiagnostics.UnparseableLine,
                        Location.None,
                        FileName(input.Path!),
                        line));
                }
            }
            else
            {
                FrontMatterReader.Read(input.Text!, FileName(input.Path!), namespaces);
            }
        }

        Validate(context, namespaces);

        foreach (KeyValuePair<string, string> source in LedgerEmitter.Emit(namespaces))
        {
            context.AddSource(source.Key, SourceText.From(source.Value, System.Text.Encoding.UTF8));
        }
    }

    /// <summary>
    /// The assembly's layer, from the MSBuild property. DD0001 reads this back out of referenced
    /// assemblies' metadata, which is why it has to be an attribute rather than a property the
    /// analyzer could only see for the project being compiled.
    /// </summary>
    private static void EmitArchLayer(SourceProductionContext context, string? archLayer)
    {
        if (archLayer is not { Length: > 0 })
        {
            return;
        }

        // A non-integer ArchLayer is the consumer's typo. Emitting nothing leaves the assembly
        // unlayered, which is what an unset property means, and DD0001 says the rest.
        if (!int.TryParse(archLayer, System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out int layer))
        {
            return;
        }

        string source = "// <auto-generated/>\n"
            + "// Emitted by DecisionDriven.Analyzers from the ArchLayer MSBuild property.\n"
            + "#nullable enable\n\n"
            + "[assembly: global::DecisionDriven.ArchLayer(" + layer.ToString(System.Globalization.CultureInfo.InvariantCulture) + ")]\n";

        context.AddSource("DecisionDriven.ArchLayer.g.cs", SourceText.From(source, System.Text.Encoding.UTF8));
    }

    /// <summary>
    /// The four things about a key that make a citation meaningful: it is an identifier, it is
    /// unique in its namespace, it can be emitted in its set, and it did not change between versions.
    /// </summary>
    private static void Validate(SourceProductionContext context, Dictionary<string, LedgerNamespace> namespaces)
    {
        List<string> namespaceNames = new List<string>(namespaces.Keys);
        namespaceNames.Sort(System.StringComparer.Ordinal);

        foreach (string namespaceName in namespaceNames)
        {
            LedgerNamespace ns = namespaces[namespaceName];

            List<string> decisionIds = new List<string>(ns.Decisions.Keys);
            decisionIds.Sort(System.StringComparer.Ordinal);

            foreach (InterimKeyClaim claim in ns.DuplicateInterimKeys)
            {
                context.ReportDiagnostic(Diagnostic.Create(
                    LedgerDiagnostics.DuplicateKey,
                    Location.None,
                    claim.Key,
                    namespaceName,
                    claim.FirstPath + " and " + claim.SecondPath));
            }

            Dictionary<string, string> keyOwners = new Dictionary<string, string>(System.StringComparer.Ordinal);

            foreach (string decisionId in decisionIds)
            {
                Decision decision = ns.Decisions[decisionId];
                DecisionVersion? tip = decision.Tip;

                if (tip is null)
                {
                    continue;
                }

                if (tip.Key is not { Length: > 0 } key)
                {
                    continue;
                }

                if (!DecisionKey.IsValid(key))
                {
                    context.ReportDiagnostic(Diagnostic.Create(
                        LedgerDiagnostics.InvalidKey,
                        Location.None,
                        key,
                        namespaceName));
                    continue;
                }

                if (tip.SetId is { Length: > 0 } setId
                    && DecisionKey.CollidingGeneratedMember(key, setId) is { } member)
                {
                    context.ReportDiagnostic(Diagnostic.Create(
                        LedgerDiagnostics.KeyCollidesWithGeneratedMember,
                        Location.None,
                        key,
                        setId,
                        namespaceName,
                        member));
                }

                if (keyOwners.TryGetValue(key, out string? owner))
                {
                    context.ReportDiagnostic(Diagnostic.Create(
                        LedgerDiagnostics.DuplicateKey,
                        Location.None,
                        key,
                        namespaceName,
                        owner + " and " + decision.Id));
                }
                else
                {
                    keyOwners.Add(key, decision.Id);
                }

                ValidateKeyStability(context, decision);
            }
        }
    }

    private static void ValidateKeyStability(SourceProductionContext context, Decision decision)
    {
        Dictionary<string, DecisionVersion> byId = new Dictionary<string, DecisionVersion>(System.StringComparer.Ordinal);
        foreach (DecisionVersion version in decision.Versions)
        {
            byId[version.Id] = version;
        }

        foreach (DecisionVersion version in decision.Versions)
        {
            if (version.RevisionOf is not { Length: > 0 } previousId)
            {
                continue;
            }

            if (!byId.TryGetValue(previousId, out DecisionVersion? previous))
            {
                continue;
            }

            if (version.Key is { Length: > 0 } key
                && previous.Key is { Length: > 0 } previousKey
                && key != previousKey)
            {
                context.ReportDiagnostic(Diagnostic.Create(
                    LedgerDiagnostics.KeyChangedBetweenVersions,
                    Location.None,
                    version.Id,
                    decision.Id,
                    key,
                    previousKey));
            }
        }
    }

    private static string FileName(string path)
    {
        int slash = path.LastIndexOfAny(new[] { '/', '\\' });
        return slash < 0 ? path : path.Substring(slash + 1);
    }

    private readonly struct LedgerInput
    {
        internal LedgerInput(string path, string kind, string text)
        {
            Path = path;
            Kind = kind;
            Text = text;
        }

        internal string? Path { get; }

        internal string? Kind { get; }

        internal string? Text { get; }
    }
}
