using System;
using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Operations;

namespace DecisionDriven.Analyzers.Rules;

/// <summary>
/// DD0003: services are wired in the composition root, not resolved where they are used.
/// </summary>
/// <remarks>
/// <para>
/// <c>StableDependencyRules.NoServiceLocationOutsideCompositionRoot</c>. Resolving from a container
/// turns a dependency the compiler could have checked into one that fails at run time, and hides it
/// from every rule that reads the reference graph - DD0001 included.
/// </para>
/// <para>
/// Matched on names rather than on symbol identity, because the DI package need not be referenced
/// for the call to be service location: a consumer's own <c>IServiceProvider</c> extension is the
/// same act. The cost is that a type of the same name in an unrelated namespace would be reported,
/// which is why the namespaces are checked too.
/// </para>
/// </remarks>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class ServiceLocationAnalyzer : DiagnosticAnalyzer
{
    private const string ServiceProviderMetadataName = "System.IServiceProvider";
    private const string ActivatorMetadataName = "System.Activator";
    private const string ActivatorUtilitiesName = "ActivatorUtilities";
    private const string DependencyInjectionNamespace = "Microsoft.Extensions.DependencyInjection";

    private static readonly ImmutableHashSet<string> ResolutionMethodNames = ImmutableHashSet.Create(
        StringComparer.Ordinal,
        "GetService",
        "GetRequiredService",
        "GetKeyedService",
        "GetRequiredKeyedService",
        "GetServices",
        "GetKeyedServices");

    /// <inheritdoc/>
    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics { get; } =
        ImmutableArray.Create(Descriptors.ServiceLocation);

    /// <inheritdoc/>
    public override void Initialize(AnalysisContext context)
    {
        context.EnableConcurrentExecution();
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);

        context.RegisterCompilationStartAction(static start =>
        {
            ArchOptions options = ArchOptions.Read(start.Options.AnalyzerConfigOptionsProvider.GlobalOptions);

            // The composition root is allowed to do exactly this, so in that project the rule
            // registers nothing at all rather than reporting and being suppressed.
            if (options.IsCompositionRoot)
            {
                return;
            }

            start.RegisterOperationAction(AnalyzeInvocation, OperationKind.Invocation);
        });
    }

    private static void AnalyzeInvocation(OperationAnalysisContext context)
    {
        IInvocationOperation invocation = (IInvocationOperation)context.Operation;
        IMethodSymbol method = invocation.TargetMethod;

        string? what = Describe(method);
        if (what is null)
        {
            return;
        }

        string finding = $"'{what}' resolves a service at run time, and this project is not a composition root";
        const string DesignChange =
            "take the dependency as a constructor parameter so the compiler can see it, "
            + "or move this wiring into the composition root and set ArchCompositionRoot=true there";

        context.ReportDiagnostic(Diagnostic.Create(
            Descriptors.ServiceLocation,
            invocation.Syntax.GetLocation(),
            finding,
            DesignChange));
    }

    /// <summary>
    /// The display name of the call when it is service location, or null when it is not.
    /// </summary>
    private static string? Describe(IMethodSymbol method)
    {
        INamedTypeSymbol? containing = method.ContainingType;
        if (containing is null)
        {
            return null;
        }

        string containingName = containing.ToDisplayString();

        if (method.Name == "CreateInstance" && containingName == ActivatorMetadataName)
        {
            return "System.Activator.CreateInstance";
        }

        if (containing.Name == ActivatorUtilitiesName
            && containing.ContainingNamespace?.ToDisplayString() == DependencyInjectionNamespace)
        {
            return containingName + "." + method.Name;
        }

        if (!ResolutionMethodNames.Contains(method.Name))
        {
            return null;
        }

        // The interface method itself.
        if (containingName == ServiceProviderMetadataName)
        {
            return ServiceProviderMetadataName + "." + method.Name;
        }

        // An extension method over IServiceProvider: the shipped ones live in the DI namespace,
        // and a consumer's own is service location just the same, so the receiver decides.
        if (method.IsExtensionMethod && method.Parameters.Length > 0)
        {
            ITypeSymbol receiver = method.Parameters[0].Type;
            if (receiver.ToDisplayString() == ServiceProviderMetadataName)
            {
                return containingName + "." + method.Name;
            }
        }

        if (method.ReducedFrom is { } reduced
            && reduced.Parameters.Length > 0
            && reduced.Parameters[0].Type.ToDisplayString() == ServiceProviderMetadataName)
        {
            return containingName + "." + method.Name;
        }

        return null;
    }
}
