using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Diagnostics;
using System.Linq;
using System.Text;
using DecisionDriven.Analyzers.Rules;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Xunit;

namespace DecisionDriven.Analyzers.Tests.Rules;

/// <summary>
/// DD0023: the namespaces of a layered compilation form a directed acyclic graph.
/// </summary>
public sealed class NamespaceCycleTests
{
    /// <summary>B refers to A through a field; what A does to refer back is each test's business.</summary>
    private const string BackEdge = "namespace Sample.B { public sealed class Back { public global::Sample.A.X? Field; } }";

    [Fact]
    public void Two_namespaces_that_refer_to_each_other_are_reported_once_per_edge()
    {
        ImmutableArray<Diagnostic> diagnostics = Run(
            "namespace Sample.A { public sealed class X { public global::Sample.B.Y? Y; } }"
            + "namespace Sample.B { public sealed class Y { public global::Sample.A.X? X; } }");

        Assert.Equal(2, diagnostics.Length);
        Assert.All(diagnostics, d => Assert.Equal("DD0023", d.Id));
        Assert.All(diagnostics, d => Assert.Equal(DiagnosticSeverity.Error, d.Severity));
        Assert.Single(diagnostics, d => d.GetMessage().Contains("namespace 'Sample.A' refers to 'Sample.B' here, and the reference comes back: Sample.B -> Sample.A", StringComparison.Ordinal));
        Assert.Single(diagnostics, d => d.GetMessage().Contains("namespace 'Sample.B' refers to 'Sample.A' here, and the reference comes back: Sample.A -> Sample.B", StringComparison.Ordinal));
    }

    [Fact]
    public void A_three_namespace_cycle_is_reported_once_per_edge_with_the_way_back()
    {
        ImmutableArray<Diagnostic> diagnostics = Run(
            "namespace Sample.A { public sealed class X { public global::Sample.B.Y? Y; } }"
            + "namespace Sample.B { public sealed class Y { public global::Sample.C.Z? Z; } }"
            + "namespace Sample.C { public sealed class Z { public global::Sample.A.X? X; } }");

        Assert.Equal(3, diagnostics.Length);
        Assert.Single(diagnostics, d => d.GetMessage().Contains("'Sample.A' refers to 'Sample.B' here, and the reference comes back: Sample.B -> Sample.C -> Sample.A", StringComparison.Ordinal));
        Assert.Single(diagnostics, d => d.GetMessage().Contains("'Sample.B' refers to 'Sample.C' here, and the reference comes back: Sample.C -> Sample.A -> Sample.B", StringComparison.Ordinal));
        Assert.Single(diagnostics, d => d.GetMessage().Contains("'Sample.C' refers to 'Sample.A' here, and the reference comes back: Sample.A -> Sample.B -> Sample.C", StringComparison.Ordinal));
    }

    [Fact]
    public void A_parent_and_its_child_namespace_are_distinct_nodes()
    {
        ImmutableArray<Diagnostic> diagnostics = Run(
            "namespace Sample.Core { public sealed class Root { public global::Sample.Core.Inner.Leaf? Leaf; } }"
            + "namespace Sample.Core.Inner { public sealed class Leaf { public global::Sample.Core.Root? Root; } }");

        Assert.Equal(2, diagnostics.Length);
        Assert.Single(diagnostics, d => d.GetMessage().Contains("'Sample.Core' refers to 'Sample.Core.Inner' here", StringComparison.Ordinal));
    }

    [Fact]
    public void A_diamond_is_a_DAG_and_is_not_reported()
    {
        Assert.Empty(Run(
            "namespace Sample.A { public sealed class X { public global::Sample.B.Y? Y; public global::Sample.C.Z? Z; } }"
            + "namespace Sample.B { public sealed class Y { public global::Sample.D.W? W; } }"
            + "namespace Sample.C { public sealed class Z { public global::Sample.D.W? W; } }"
            + "namespace Sample.D { public sealed class W { } }"));
    }

    [Fact]
    public void A_reference_within_one_namespace_is_not_an_edge()
    {
        Assert.Empty(Run("namespace Sample.A { public sealed class X { public Y? Y; } public sealed class Y { public X? X; } }"));
    }

    [Theory]
    [InlineData("base type", "public class X : global::Sample.B.Open { }")]
    [InlineData("interface", "public sealed class X : global::Sample.B.IY { }")]
    [InlineData("attribute", "[global::Sample.B.Mark] public sealed class X { }")]
    [InlineData("attribute on a member", "public sealed class X { [global::Sample.B.Mark] public void M() { } }")]
    [InlineData("return type", "public sealed class X { public global::Sample.B.Y? M() => null; }")]
    [InlineData("parameter", "public sealed class X { public void M(global::Sample.B.Y y) { } }")]
    [InlineData("property", "public sealed class X { public global::Sample.B.Y? P { get; set; } }")]
    [InlineData("field", "public sealed class X { public global::Sample.B.Y? F; } ")]
    [InlineData("event", "public sealed class X { public event global::Sample.B.Handler? E; }")]
    [InlineData("delegate signature", "public delegate global::Sample.B.Y X();")]
    [InlineData("generic argument", "public sealed class X { public System.Collections.Generic.List<global::Sample.B.Y>? L; }")]
    [InlineData("constraint", "public sealed class X { public void M<T>() where T : global::Sample.B.Open { } }")]
    [InlineData("static member access", "public sealed class X { public int M() => global::Sample.B.Y.Count; }")]
    [InlineData("constant", "public sealed class X { public string M() => global::Sample.B.Y.Name; }")]
    [InlineData("constant in a static lambda", "public sealed class X { public void M(System.Action<string> add) => add(global::Sample.B.Y.Name); }")]
    [InlineData("static field in a static lambda", "public sealed class X { public System.Func<int> F = static () => global::Sample.B.Y.Count; }")]
    [InlineData("static invocation", "public sealed class X { public void M() => global::Sample.B.Y.Make(); }")]
    [InlineData("extension method", "public sealed class X { public int M() => 1.Twice(); }")]
    [InlineData("typeof", "public sealed class X { public System.Type M() => typeof(global::Sample.B.Y); }")]
    [InlineData("nameof", "public sealed class X { public string M() => nameof(global::Sample.B.Y); }")]
    [InlineData("nameof a member", "public sealed class X { public string M() => nameof(global::Sample.B.Y.Count); }")]
    [InlineData("is", "public sealed class X { public bool M(object o) => o is global::Sample.B.Y; }")]
    [InlineData("is pattern", "public sealed class X { public bool M(object o) => o is global::Sample.B.Y { Count2: 0 }; }")]
    [InlineData("as", "public sealed class X { public object? M(object o) => o as global::Sample.B.Y; }")]
    [InlineData("cast", "public sealed class X { public object M(object o) => (global::Sample.B.Y)o; }")]
    [InlineData("object creation", "public sealed class X { public object M() => new global::Sample.B.Y(); }")]
    [InlineData("local", "public sealed class X { public void M() { global::Sample.B.Y? y = null; _ = y; } }")]
    [InlineData("field initialiser", "public sealed class X { public object F = new global::Sample.B.Y(); }")]
    [InlineData("lambda parameter", "public sealed class X { public System.Delegate F = (global::Sample.B.Y y) => 0; }")]
    [InlineData("local function", "public sealed class X { public void M() { static global::Sample.B.Y? L() => null; _ = L(); } }")]
    [InlineData("catch", "public sealed class X { public void M() { try { } catch (global::Sample.B.Oops) { } } }")]
    [InlineData("default", "public sealed class X { public object? M() => default(global::Sample.B.Y); }")]
    [InlineData("array", "public sealed class X { public object M() => new global::Sample.B.Y[0]; }")]
    [InlineData("generic method argument", "public sealed class X { public void M() => Run<global::Sample.B.Y>(); private static void Run<T>() { } }")]
    public void Each_kind_of_reference_is_an_edge(string kind, string declarationInA)
    {
        ImmutableArray<Diagnostic> diagnostics = Run(
            "using Sample.B;"
            + "namespace Sample.A { " + declarationInA + " }"
            + "namespace Sample.B {"
            + "  public sealed class Y { public const string Name = \"y\"; public static int Count; public int Count2; public static void Make() { } }"
            + "  public class Open { }"
            + "  public interface IY { }"
            + "  public sealed class MarkAttribute : System.Attribute { }"
            + "  public delegate void Handler();"
            + "  public sealed class Oops : System.Exception { }"
            + "  public static class Ext { public static int Twice(this int i) => i * 2; }"
            + "  public sealed class Back { public global::Sample.A.X? Field; }"
            + "}");

        Assert.True(diagnostics.Length == 2, kind + ": " + string.Join(" / ", diagnostics.Select(d => d.GetMessage())));
        Assert.Single(diagnostics, d => d.GetMessage().Contains("namespace 'Sample.A' refers to 'Sample.B' here", StringComparison.Ordinal));
    }

    [Fact]
    public void A_nested_types_references_belong_to_the_outer_types_namespace()
    {
        ImmutableArray<Diagnostic> diagnostics = Run(
            "namespace Sample.A { public sealed class Outer { public sealed class Inner { public global::Sample.B.Y? Y; } } }"
            + "namespace Sample.B { public sealed class Y { public global::Sample.A.Outer.Inner? Inner; } }");

        Assert.Equal(2, diagnostics.Length);
        Assert.Single(diagnostics, d => d.GetMessage().Contains("namespace 'Sample.A' refers to 'Sample.B' here", StringComparison.Ordinal));
        Assert.Single(diagnostics, d => d.GetMessage().Contains("namespace 'Sample.B' refers to 'Sample.A' here", StringComparison.Ordinal));
    }

    [Fact]
    public void A_using_directive_alone_is_not_a_reference()
    {
        Assert.Empty(Run("using Sample.B; namespace Sample.A { public sealed class X { } } " + BackEdge));
    }

    [Fact]
    public void Generated_code_contributes_no_edges()
    {
        // RuleTiers.GeneratedCodeIsExempt: the generated file is what refers from A to B, and the
        // edge it would make is the one that would close the cycle.
        CSharpCompilation compilation = Compile(
            ("/proj/A/X.cs", "namespace Sample.A { public sealed partial class X { } }"),
            ("/proj/A/X.g.cs", "namespace Sample.A { public sealed partial class X { public global::Sample.B.Back? Back; } }"),
            ("/proj/B/Back.cs", BackEdge));

        Assert.Empty(RuleHarness.RunOn(new NamespaceCycleAnalyzer(), compilation, Layered()));

        // The control: the same reference in a hand-written file closes it.
        CSharpCompilation byHand = Compile(
            ("/proj/A/X.cs", "namespace Sample.A { public sealed partial class X { } }"),
            ("/proj/A/X2.cs", "namespace Sample.A { public sealed partial class X { public global::Sample.B.Back? Back; } }"),
            ("/proj/B/Back.cs", BackEdge));

        Assert.Equal(2, RuleHarness.RunOn(new NamespaceCycleAnalyzer(), byHand, Layered()).Length);
    }

    [Fact]
    public void A_project_with_no_layer_is_not_checked()
    {
        Assert.Empty(RuleHarness.Run(
            new NamespaceCycleAnalyzer(),
            "namespace Sample.A { public sealed class X { public global::Sample.B.Y? Y; } } namespace Sample.B { public sealed class Y { public global::Sample.A.X? X; } }",
            assemblyName: "Sample.Core"));
    }

    [Fact]
    public void DesignDecision_on_a_type_removes_its_outgoing_edges()
    {
        const string Attribute = "[global::DecisionDriven.DesignDecision(typeof(object), Scope = global::DecisionDriven.ExceptionScope.Migration)]";

        // A's only reference to B is from the marked type, so the cycle is gone; B still refers to A.
        Assert.Empty(Run(ContractSource.File(
            "namespace Sample.A { " + Attribute + " public sealed class X { public global::Sample.B.Y? Y; } }"
            + "namespace Sample.B { public sealed class Y { public global::Sample.A.X? X; } }")));

        // A second, unmarked type in A keeps the edge, and the cycle.
        Assert.Equal(2, Run(ContractSource.File(
            "namespace Sample.A { " + Attribute + " public sealed class X { public global::Sample.B.Y? Y; } public sealed class Z { public global::Sample.B.Y? Y; } }"
            + "namespace Sample.B { public sealed class Y { public global::Sample.A.X? X; } }")).Length);
    }

    [Fact]
    public void An_edge_is_reported_at_its_first_location_by_file_then_span()
    {
        CSharpCompilation compilation = Compile(
            ("/proj/A/Second.cs", "namespace Sample.A { public sealed class Z { public global::Sample.B.Back? B1; } }"),
            ("/proj/A/First.cs", "namespace Sample.A { public sealed class X { public global::Sample.B.Back? B2; public global::Sample.B.Back? B3; } }"),
            ("/proj/B/Back.cs", BackEdge));

        ImmutableArray<Diagnostic> diagnostics = RuleHarness.RunOn(new NamespaceCycleAnalyzer(), compilation, Layered());

        Diagnostic forward = Assert.Single(diagnostics, d => d.GetMessage().Contains("'Sample.A' refers to 'Sample.B'", StringComparison.Ordinal));
        // First.cs sorts before Second.cs, and in it the field B2 is the first thing that refers to B.
        Assert.Equal("/proj/A/First.cs", forward.Location.SourceTree!.FilePath);
        Assert.Equal("B2", forward.Location.SourceTree.GetText(TestContext.Current.CancellationToken).ToString(forward.Location.SourceSpan));
    }

    [Fact]
    public void The_message_is_exactly_this()
    {
        // DiagnosticMessages.ExactMessageTested.
        ImmutableArray<Diagnostic> diagnostics = Run(
            "namespace Sample.Core.Ping { public sealed class Ping { public global::Sample.Core.Pong.Pong? Pong; } }"
            + "namespace Sample.Core.Pong { public sealed class Pong { public global::Sample.Core.Ping.Ping? Ping; } }");

        Assert.Equal(
            "namespace 'Sample.Core.Ping' refers to 'Sample.Core.Pong' here, and the reference comes back: Sample.Core.Pong -> Sample.Core.Ping. "
            + "Decide: move the type that closes the cycle into the namespace that uses it, "
            + "or merge the two namespaces, which is what the cycle says they are "
            + "| mark it [DesignDecision(typeof(<Set>.<Key>), Scope = ExceptionScope.<Scope>)] "
            + "citing the accepted decision that says so. "
            + "Do not add the attribute without a decision that answers this; if the reason is only that "
            + "the code already looked like this, take the design change.",
            diagnostics.First(d => d.GetMessage().StartsWith("namespace 'Sample.Core.Ping'", StringComparison.Ordinal)).GetMessage());
    }

    [Fact]
    public void A_few_hundred_types_across_many_namespaces_are_analysed_in_bounded_time()
    {
        // No threshold is set; the run time is recorded in the session close-out. 40 namespaces of
        // 10 types each, every type referring to two types in the next namespace, plus one cycle at
        // the end so that the report path is exercised too.
        const int Namespaces = 40;
        const int TypesPer = 10;
        StringBuilder source = new StringBuilder();

        for (int n = 0; n < Namespaces; n++)
        {
            source.Append("namespace Sample.N").Append(n).Append(" {");
            for (int t = 0; t < TypesPer; t++)
            {
                source.Append(" public sealed class T").Append(t).Append(" {");
                if (n + 1 < Namespaces)
                {
                    source.Append(" public global::Sample.N").Append(n + 1).Append(".T").Append(t).Append("? Next;");
                    source.Append(" public global::Sample.N").Append(n + 1).Append(".T").Append((t + 1) % TypesPer).Append("? Other;");
                    source.Append(" public object Make() => new global::Sample.N").Append(n + 1).Append(".T").Append(t).Append("();");
                }

                source.Append(" }");
            }

            source.Append(" }");
        }

        source.Append("namespace Sample.N").Append(Namespaces - 1).Append(" { public sealed class Loop { public global::Sample.N0.T0? Back; } }");

        Stopwatch watch = Stopwatch.StartNew();
        ImmutableArray<Diagnostic> diagnostics = Run(source.ToString());
        watch.Stop();

        TestContext.Current.TestOutputHelper?.WriteLine(
            "DD0023 over " + (Namespaces * TypesPer + 1) + " types in " + Namespaces + " namespaces: " + watch.ElapsedMilliseconds + " ms");

        // Every edge of the 40-namespace ring is on the one cycle.
        Assert.Equal(Namespaces, diagnostics.Length);
    }

    private static ImmutableArray<Diagnostic> Run(string source) =>
        RuleHarness.Run(new NamespaceCycleAnalyzer(), source, assemblyName: "Sample.Core", archLayer: 1);

    private static Dictionary<string, string> Layered() =>
        new Dictionary<string, string>(StringComparer.Ordinal) { ["build_property.ArchLayer"] = "1" };

    private static CSharpCompilation Compile(params (string Path, string Source)[] files) =>
        CSharpCompilation.Create(
            "Sample.Core",
            files.Select(file => CSharpSyntaxTree.ParseText(file.Source, new CSharpParseOptions(LanguageVersion.Latest), file.Path)),
            RuleHarness.PlatformReferences(),
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, nullableContextOptions: NullableContextOptions.Enable));
}
