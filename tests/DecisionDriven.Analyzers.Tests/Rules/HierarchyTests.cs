using System;
using System.Collections.Immutable;
using DecisionDriven.Analyzers.Rules;
using Microsoft.CodeAnalysis;
using Xunit;

namespace DecisionDriven.Analyzers.Tests.Rules;

/// <summary>
/// DD0017 and DD0018: hierarchies nothing closed, and placeholder bodies.
/// </summary>
public sealed class HierarchyTests
{
    private const string Open =
        "public abstract class Node { } "
        + "public sealed class Add : Node { } "
        + "public sealed class Mul : Node { } ";

    private const string Closed =
        "public abstract class Node { private Node() { } "
        + "public sealed class Add : Node { } "
        + "public sealed class Mul : Node { } } ";

    [Fact]
    public void A_switch_statement_over_an_open_hierarchy_is_warned_about()
    {
        Diagnostic diagnostic = Assert.Single(Switch(
            Open + "public static class Eval { public static int Of(Node n) { "
            + "switch (n) { case Add a: return 1; case Mul m: return 2; default: return 0; } } }"));

        Assert.Equal("DD0017", diagnostic.Id);
        Assert.Equal(DiagnosticSeverity.Warning, diagnostic.Severity);
        Assert.Contains("nothing closes 'Consumer.Node'", diagnostic.GetMessage(), StringComparison.Ordinal);
    }

    [Fact]
    public void A_switch_expression_over_an_open_hierarchy_is_warned_about()
    {
        Assert.Single(Switch(
            Open + "public static class Eval { public static int Of(Node n) => "
            + "n switch { Add => 1, Mul => 2, _ => 0 }; }"));
    }

    [Fact]
    public void A_switch_over_a_closed_hierarchy_is_not_warned_about()
    {
        // A private constructor closes it outright: only nested types can call one, and they are
        // in this file. The compiler then checks the switch is exhaustive, which is the point.
        Assert.Empty(Switch(
            Closed + "public static class Eval { public static int Of(Node n) => "
            + "n switch { Node.Add => 1, Node.Mul => 2, _ => 0 }; }"));
    }

    [Fact]
    public void A_switch_over_a_file_local_base_is_not_warned_about()
    {
        Assert.Empty(Switch(
            "file abstract class Shape { } "
            + "file sealed class Circle : Shape { } "
            + "file sealed class Square : Shape { } "
            + "public static class Area { internal static int Of(object o) => "
            + "o switch { Circle => 1, Square => 2, _ => 0 }; }"));
    }

    [Fact]
    public void An_internal_constructor_with_sealed_leaves_is_closed()
    {
        Assert.Empty(Switch(
            "public abstract class Node { internal Node() { } } "
            + "public sealed class Add : Node { } "
            + "public sealed class Mul : Node { } "
            + "public static class Eval { public static int Of(Node n) => "
            + "n switch { Add => 1, Mul => 2, _ => 0 }; }"));
    }

    [Fact]
    public void An_abstract_record_with_a_private_protected_constructor_and_sealed_leaves_is_closed()
    {
        // The case a consumer met: a query algebra of abstract record bases with private protected
        // constructors and sealed record leaves. The compiler adds a protected copy constructor to
        // every non-sealed record and forbids declaring it narrower (CS8878), so before the copy
        // constructor was set aside this hierarchy could never be closed.
        Assert.Empty(Switch(
            "public abstract record Pattern { private protected Pattern() { } } "
            + "public sealed record Join(Pattern Left, Pattern Right) : Pattern; "
            + "public sealed record Union(Pattern Left, Pattern Right) : Pattern; "
            + "public static class Eval { public static int Of(Pattern p) => "
            + "p switch { Join => 1, Union => 2, _ => 0 }; }"));
    }

    [Fact]
    public void An_abstract_record_with_no_declared_constructor_is_still_open()
    {
        // Setting the copy constructor aside does not close a record by itself: without a narrower
        // constructor, the implicit one is protected and anyone can derive.
        Assert.Single(Switch(
            "public abstract record Pattern; "
            + "public sealed record Join(Pattern Left, Pattern Right) : Pattern; "
            + "public sealed record Union(Pattern Left, Pattern Right) : Pattern; "
            + "public static class Eval { public static int Of(Pattern p) => "
            + "p switch { Join => 1, Union => 2, _ => 0 }; }"));
    }

    [Fact]
    public void An_abstract_record_with_an_unsealed_record_leaf_is_still_open()
    {
        Assert.Single(Switch(
            "public abstract record Pattern { private protected Pattern() { } } "
            + "public record Join(Pattern Left, Pattern Right) : Pattern; "
            + "public sealed record Union(Pattern Left, Pattern Right) : Pattern; "
            + "public static class Eval { public static int Of(Pattern p) => "
            + "p switch { Join => 1, Union => 2, _ => 0 }; }"));
    }

    [Fact]
    public void An_internal_base_with_sealed_leaves_is_closed()
    {
        // Nobody outside the assembly can derive from a type they cannot name, whatever its
        // constructor says. ADR-A10: closed when "the base is not public-derivable outside the
        // assembly" and every derived type in the compilation is sealed.
        Assert.Empty(Switch(
            "internal abstract class Node { } "
            + "internal sealed class Add : Node { } "
            + "internal sealed class Mul : Node { } "
            + "internal static class Eval { internal static int Of(Node n) => "
            + "n switch { Add => 1, Mul => 2, _ => 0 }; }"));
    }

    [Fact]
    public void An_internal_base_with_an_unsealed_leaf_is_not_closed()
    {
        Assert.Single(Switch(
            "internal abstract class Node { } "
            + "internal sealed class Add : Node { } "
            + "internal class Mul : Node { } "
            + "internal static class Eval { internal static int Of(Node n) => "
            + "n switch { Add => 1, Mul => 2, _ => 0 }; }"));
    }

    [Fact]
    public void An_internal_constructor_with_an_unsealed_leaf_is_not_closed()
    {
        Assert.Single(Switch(
            "public abstract class Node { internal Node() { } } "
            + "public class Add : Node { } "
            + "public sealed class Mul : Node { } "
            + "public static class Eval { public static int Of(Node n) => "
            + "n switch { Add => 1, Mul => 2, _ => 0 }; }"));
    }

    [Theory]
    [InlineData("System.ArgumentException", "System.InvalidOperationException")]
    [InlineData("System.IO.MemoryStream", "System.IO.FileStream")]
    public void A_switch_over_a_framework_hierarchy_is_not_warned_about(string first, string second)
    {
        // Hierarchies.TypeSwitchOverOpenHierarchyWarning excludes these by default. Exception and
        // Stream are open by design and a consumer cannot close them; a warning nobody can act on
        // is a warning people learn to skip past, including past the ones that matter.
        Assert.Empty(Switch(
            "public static class Handle { public static int Of(object o) => "
            + "o switch { " + first + " => 1, " + second + " => 2, _ => 0 }; }"));
    }

    [Fact]
    public void A_discard_arm_that_throws_is_still_warned_about()
    {
        // The case the rule is about, not a defence against it: the next subtype reaches the throw,
        // and the throw is at run time in front of a user rather than at compile time in front of
        // whoever added the subtype.
        Assert.Single(Switch(
            Open + "public static class Eval { public static int Of(Node n) => "
            + "n switch { Add => 1, Mul => 2, _ => throw new System.ArgumentOutOfRangeException(nameof(n)) }; }"));
    }

    [Fact]
    public void One_tested_type_is_not_a_claim_about_a_set()
    {
        Assert.Empty(Switch(
            Open + "public static class Eval { public static int Of(Node n) => "
            + "n switch { Add => 1, _ => 0 }; }"));
    }

    [Fact]
    public void The_open_hierarchy_message_is_exactly_this()
    {
        // DiagnosticMessages.ExactMessageTested.
        Assert.Equal(
            "this switch tests 2 subtypes of 'Consumer.Node', and nothing closes 'Consumer.Node'. "
            + "Decide: close the hierarchy - give 'Consumer.Node' a private or file constructor and "
            + "seal its leaves, so the compiler checks the switch is exhaustive - or replace the "
            + "switch with a virtual member on the base "
            + "| mark it [DesignDecision(typeof(<Set>.<Key>), Scope = ExceptionScope.<Scope>)] "
            + "citing the accepted decision that says so. "
            + "Do not add the attribute without a decision that answers this; if the reason is only that "
            + "the code already looked like this, take the design change.",
            Assert.Single(Switch(
                Open + "public static class Eval { public static int Of(Node n) => "
                + "n switch { Add => 1, Mul => 2, _ => 0 }; }")).GetMessage());
    }

    [Fact]
    public void A_not_implemented_exception_in_a_method_is_reported()
    {
        Diagnostic diagnostic = Assert.Single(Placeholder(
            "public sealed class Thing { public int Read() => throw new System.NotImplementedException(); }"));

        Assert.Equal("DD0018", diagnostic.Id);
        Assert.Equal(DiagnosticSeverity.Error, diagnostic.Severity);
        Assert.Contains("'Consumer.Thing.Read' has a NotImplementedException", diagnostic.GetMessage(), StringComparison.Ordinal);
    }

    [Fact]
    public void A_not_implemented_exception_in_a_property_getter_is_reported()
    {
        // Reported as the property: the getter has no name a reader would recognise.
        Diagnostic diagnostic = Assert.Single(Placeholder(
            "public sealed class Thing { public int Count { get { throw new System.NotImplementedException(); } } }"));

        Assert.Contains("'Consumer.Thing.Count'", diagnostic.GetMessage(), StringComparison.Ordinal);
    }

    [Fact]
    public void A_not_implemented_exception_in_a_lambda_is_reported()
    {
        Assert.Single(Placeholder(
            "public sealed class Thing { public System.Func<int> Read() => "
            + "() => throw new System.NotImplementedException(); }"));
    }

    [Fact]
    public void A_not_implemented_exception_in_a_test_assembly_is_not_reported()
    {
        // A test that asserts a member throws has to be able to write the throw.
        Assert.Empty(Placeholder(
            "public sealed class Thing { public int Read() => throw new System.NotImplementedException(); }",
            assemblyName: "Consumer.Tests"));
    }

    [Fact]
    public void A_not_implemented_exception_in_a_project_with_no_declared_layer_is_reported()
    {
        // Unlike the contract rules this one is not gated on ArchLayer. A project that never
        // declared a layer is where a placeholder is most likely to survive.
        Assert.Single(Placeholder(
            "public sealed class Thing { public int Read() => throw new System.NotImplementedException(); }",
            archLayer: null));
    }

    [Fact]
    public void A_not_implemented_exception_in_a_composition_root_is_reported()
    {
        Assert.Single(RuleHarness.Run(
            new NotImplementedAnalyzer(),
            ContractSource.File("namespace Consumer { public sealed class Thing { public int Read() => throw new System.NotImplementedException(); } }"),
            assemblyName: "Consumer",
            archLayer: 3,
            compositionRoot: true));
    }

    [Fact]
    public void A_not_supported_exception_is_not_this_rules_finding()
    {
        // That is the shape a deliberate stub takes, and DD0012 is what tracks it.
        Assert.Empty(Placeholder(
            "public sealed class Thing { public int Read() => throw new System.NotSupportedException(); }"));
    }

    [Fact]
    public void One_throw_is_one_finding()
    {
        Assert.Single(Placeholder(
            "public sealed class Thing { public int Read() { throw new System.NotImplementedException(); } }"));
    }

    [Fact]
    public void The_placeholder_message_is_exactly_this()
    {
        // DiagnosticMessages.ExactMessageTested. This rule's second path is not [DesignDecision]:
        // a citation on a placeholder makes it permanent and still invisible.
        Assert.Equal(
            "'Consumer.Thing.Read' has a NotImplementedException in it. "
            + "Decide: write the member, or take it off the type until there is something to write "
            + "| if it is a deliberate stub, make it a NotSupportedException marked "
            + "[DesignDecision(typeof(<Set>.<Key>), Scope = ExceptionScope.<Scope>)], which DD0012 "
            + "tracks and the report tool lists. "
            + "Do not leave it as it is; a placeholder nobody can find is a placeholder that ships.",
            Assert.Single(Placeholder(
                "public sealed class Thing { public int Read() => throw new System.NotImplementedException(); }"))
                .GetMessage());
    }

    private static ImmutableArray<Diagnostic> Switch(string body) =>
        RuleHarness.Run(
            new OpenHierarchyAnalyzer(),
            ContractSource.File("namespace Consumer { " + body + " }"),
            assemblyName: "Consumer",
            archLayer: 1);

    private static ImmutableArray<Diagnostic> Placeholder(string body, string assemblyName = "Consumer", int? archLayer = 1) =>
        RuleHarness.Run(
            new NotImplementedAnalyzer(),
            ContractSource.File("namespace Consumer { " + body + " }"),
            assemblyName: assemblyName,
            archLayer: archLayer);
}
