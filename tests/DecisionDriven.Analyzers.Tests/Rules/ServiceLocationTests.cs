using System.Collections.Immutable;
using DecisionDriven.Analyzers.Rules;
using Microsoft.CodeAnalysis;
using Xunit;

namespace DecisionDriven.Analyzers.Tests.Rules;

/// <summary>
/// DD0003: services are wired in the composition root, not resolved where they are used.
/// </summary>
public sealed class ServiceLocationTests
{
    /// <summary>
    /// A stand-in for the DI package, which this repository does not reference. The rule matches on
    /// names and receiver types rather than on symbol identity precisely so that a consumer's own
    /// extension counts too, and this is where that claim is tested.
    /// </summary>
    private const string DependencyInjection = """
        namespace Microsoft.Extensions.DependencyInjection
        {
            internal static class ServiceProviderServiceExtensions
            {
                public static T GetRequiredService<T>(this global::System.IServiceProvider provider) => default!;
            }

            internal static class ActivatorUtilities
            {
                public static object CreateInstance(global::System.IServiceProvider provider, global::System.Type type) => null!;
            }
        }
        """;

    [Fact]
    public void Constructor_injection_is_not_reported()
    {
        // The conforming sample: the dependency is a parameter, so the compiler can see it and so
        // can every rule that reads the reference graph.
        Assert.Empty(Run("""
            internal sealed class Service
            {
                private readonly string dependency;
                public Service(string dependency) => this.dependency = dependency;
            }
            """));
    }

    [Fact]
    public void Calling_GetService_on_IServiceProvider_is_reported()
    {
        Diagnostic diagnostic = Assert.Single(Run("""
            internal sealed class Service
            {
                public object? Resolve(global::System.IServiceProvider provider) => provider.GetService(typeof(string));
            }
            """));

        Assert.Equal("DD0003", diagnostic.Id);
        Assert.Equal(DiagnosticSeverity.Error, diagnostic.Severity);
    }

    [Fact]
    public void Calling_an_extension_over_IServiceProvider_is_reported()
    {
        Diagnostic diagnostic = Assert.Single(Run(DependencyInjection + """

            internal sealed class Service
            {
                public string Resolve(global::System.IServiceProvider provider) =>
                    global::Microsoft.Extensions.DependencyInjection.ServiceProviderServiceExtensions.GetRequiredService<string>(provider);
            }
            """));

        Assert.Equal("DD0003", diagnostic.Id);
    }

    [Fact]
    public void Calling_ActivatorUtilities_is_reported()
    {
        Assert.Single(Run(DependencyInjection + """

            internal sealed class Service
            {
                public object Make(global::System.IServiceProvider provider) =>
                    global::Microsoft.Extensions.DependencyInjection.ActivatorUtilities.CreateInstance(provider, typeof(string));
            }
            """));
    }

    [Fact]
    public void Calling_Activator_CreateInstance_is_reported()
    {
        Assert.Single(Run("""
            internal sealed class Service
            {
                public object? Make() => global::System.Activator.CreateInstance(typeof(string));
            }
            """));
    }

    [Fact]
    public void A_method_named_GetService_on_an_unrelated_type_is_not_reported()
    {
        // The false-positive story for this rule: the names are common. What makes a call service
        // location is the receiver, not the verb.
        Assert.Empty(Run("""
            internal sealed class Catalogue
            {
                public string GetService(string name) => name;
            }

            internal sealed class Service
            {
                public string Use(Catalogue catalogue) => catalogue.GetService("x");
            }
            """));
    }

    [Fact]
    public void The_composition_root_is_allowed_to_resolve()
    {
        // The configuration knob. The composition root is the one project whose job is wiring, and
        // it says so with ArchCompositionRoot rather than by suppressing the rule.
        Assert.Empty(RuleHarness.Run(
            new ServiceLocationAnalyzer(),
            """
            internal sealed class Host
            {
                public object? Resolve(global::System.IServiceProvider provider) => provider.GetService(typeof(string));
            }
            """,
            assemblyName: "Sample.Host",
            compositionRoot: true));
    }

    [Fact]
    public void The_message_for_the_canonical_violating_sample_is_exact()
    {
        // DiagnosticMessages.ExactMessageTested.
        Diagnostic diagnostic = Assert.Single(Run("""
            internal sealed class Service
            {
                public object? Resolve(global::System.IServiceProvider provider) => provider.GetService(typeof(string));
            }
            """));

        Assert.Equal(
            "'System.IServiceProvider.GetService' resolves a service at run time, and this project is not a composition root. "
            + "Decide: take the dependency as a constructor parameter so the compiler can see it, "
            + "or move this wiring into the composition root and set ArchCompositionRoot=true there "
            + "| mark it [DesignDecision(typeof(<Set>.<Key>), Scope = ExceptionScope.<Scope>)] "
            + "citing the accepted decision that says so. "
            + "Do not add the attribute without a decision that answers this; "
            + "if the reason is only that the code already looked like this, take the design change.",
            diagnostic.GetMessage());
    }

    private static ImmutableArray<Diagnostic> Run(string source) =>
        RuleHarness.Run(new ServiceLocationAnalyzer(), source, assemblyName: "Sample.Layer1");
}
