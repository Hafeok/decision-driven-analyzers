using System;
using System.Collections.Immutable;
using DecisionDriven.Analyzers.Rules;
using Microsoft.CodeAnalysis;
using Xunit;

namespace DecisionDriven.Analyzers.Tests.Rules;

/// <summary>
/// DD0009: every public interface, abstract class and delegate cites a decision.
/// </summary>
public sealed class ContractDeclarationTests
{
    [Fact]
    public void A_public_interface_without_a_contract_is_reported()
    {
        Diagnostic diagnostic = Assert.Single(Run("public interface IQuadSource { int Read(); }"));

        Assert.Equal("DD0009", diagnostic.Id);
        Assert.Equal(DiagnosticSeverity.Error, diagnostic.Severity);
        Assert.Contains("public interface 'Consumer.IQuadSource'", diagnostic.GetMessage(), StringComparison.Ordinal);
    }

    [Fact]
    public void A_public_abstract_class_without_a_contract_is_reported()
    {
        Diagnostic diagnostic = Assert.Single(Run("public abstract class Reader { public abstract int Read(); }"));

        Assert.Contains("public abstract class 'Consumer.Reader'", diagnostic.GetMessage(), StringComparison.Ordinal);
    }

    [Fact]
    public void A_public_delegate_without_a_contract_is_reported()
    {
        Diagnostic diagnostic = Assert.Single(Run("public delegate int Read(int offset);"));

        Assert.Contains("public delegate 'Consumer.Read'", diagnostic.GetMessage(), StringComparison.Ordinal);
    }

    [Fact]
    public void A_generic_public_interface_without_a_contract_is_reported()
    {
        // Named with its type parameters, because an assembly with ISource and ISource<T> in it
        // has two findings and a message naming 'ISource' twice has said nothing.
        Diagnostic diagnostic = Assert.Single(Run("public interface ISource<T> { T Read(); }"));

        Assert.Contains("public interface 'Consumer.ISource<T>'", diagnostic.GetMessage(), StringComparison.Ordinal);
    }

    [Fact]
    public void A_contract_marked_type_is_not_reported()
    {
        Assert.Empty(Run(ContractSource.Contract + " public interface IQuadSource { int Read(); }"));
    }

    [Fact]
    public void An_internal_interface_is_not_reported()
    {
        // Not a surface. Nobody outside the assembly can name it, so nothing outside can depend on it.
        Assert.Empty(Run("internal interface IQuadSource { int Read(); }"));
    }

    [Fact]
    public void A_public_interface_nested_in_an_internal_type_is_not_reported()
    {
        Assert.Empty(Run("internal static class Holder { public interface IQuadSource { int Read(); } }"));
    }

    [Fact]
    public void A_plain_public_class_is_not_reported()
    {
        // The rule is about types somebody implements, not every public type. DD0019 is where
        // public model types get their own answer.
        Assert.Empty(Run("public sealed class Quad { public int Value { get; } }"));
    }

    [Fact]
    public void A_project_with_no_declared_layer_is_not_checked()
    {
        // ADR-A08 scopes the contract rules to projects with ArchLayer declared. Reporting one
        // that never declared a layer would be the package deciding a consumer has adopted it.
        Assert.Empty(Run("public interface IQuadSource { int Read(); }", archLayer: null));
    }

    [Fact]
    public void A_composition_root_is_not_checked()
    {
        Assert.Empty(Run("public interface IQuadSource { int Read(); }", compositionRoot: true));
    }

    [Fact]
    public void A_test_assembly_is_not_checked()
    {
        Assert.Empty(Run("public interface IQuadSource { int Read(); }", assemblyName: "Consumer.Tests"));
    }

    [Fact]
    public void The_message_is_exactly_this()
    {
        // DiagnosticMessages.ExactMessageTested.
        Diagnostic diagnostic = Assert.Single(Run("public interface IQuadSource { int Read(); }"));

        Assert.Equal(
            "public interface 'Consumer.IQuadSource' is part of this assembly's surface and cites no decision. "
            + "Decide: make it internal if nothing outside needs it, or mark it "
            + "[Contract(typeof(<Set>.<Key>), Role = \"...\")] citing the decision that says this is a contract "
            + "| mark it [DesignDecision(typeof(<Set>.<Key>), Scope = ExceptionScope.<Scope>)] "
            + "citing the accepted decision that says so. "
            + "Do not add the attribute without a decision that answers this; if the reason is only that "
            + "the code already looked like this, take the design change.",
            diagnostic.GetMessage());
    }

    private static ImmutableArray<Diagnostic> Run(
        string body,
        int? archLayer = 1,
        bool compositionRoot = false,
        string assemblyName = "Consumer") =>
        RuleHarness.Run(
            new ContractDeclarationAnalyzer(),
            ContractSource.File("namespace Consumer { " + body + " }"),
            assemblyName: assemblyName,
            archLayer: archLayer,
            compositionRoot: compositionRoot);
}
