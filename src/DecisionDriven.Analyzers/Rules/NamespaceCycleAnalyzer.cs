using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Operations;

namespace DecisionDriven.Analyzers.Rules;

/// <summary>
/// DD0023: the namespaces of a layered compilation form a directed acyclic graph.
/// </summary>
/// <remarks>
/// <para>
/// <c>StableDependencyRules.NamespacesAcyclicWithinAssembly</c>. DD0001 keeps references between
/// assemblies pointing one way. Inside an assembly the namespaces are the structure, and two of them
/// that reference each other are one namespace with two names. The rule applies to every
/// compilation with <c>ArchLayer</c> set, like DD0020, and has no switch of its own: an
/// <c>.editorconfig</c> switch on a tier-1 rule is a suppression path (ADR-A17).
/// </para>
/// <para>
/// Edges are collected as the compilation is analysed - a symbol action for what a declaration's
/// signature names, an operation action for what its bodies and initialisers bind - into one
/// concurrent map keyed by the pair of namespaces, each holding the first location that made the
/// edge. At compilation end the graph's strongly connected components are computed and every edge
/// inside a non-trivial one is reported, once, at that location. Nothing is rescanned at the end.
/// </para>
/// <para>
/// A nested type belongs to the namespace of its outermost containing type, and parent and child
/// namespaces are distinct nodes. A <c>using</c> directive is not a reference. Generated code
/// contributes no edges (<c>RuleTiers.GeneratedCodeIsExempt</c>). <c>[DesignDecision]</c> on a
/// type removes that type's outgoing edges.
/// </para>
/// </remarks>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class NamespaceCycleAnalyzer : DiagnosticAnalyzer
{
    private const string DesignChange =
        "move the type that closes the cycle into the namespace that uses it, "
        + "or merge the two namespaces, which is what the cycle says they are";

    /// <inheritdoc/>
    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics { get; } =
        ImmutableArray.Create(Descriptors.NamespaceCycle);

    /// <inheritdoc/>
    public override void Initialize(AnalysisContext context)
    {
        context.EnableConcurrentExecution();
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);

        context.RegisterCompilationStartAction(static start =>
        {
            ArchOptions options = ArchOptions.Read(start.Options.AnalyzerConfigOptionsProvider.GlobalOptions);

            if (options.Layer is null)
            {
                return;
            }

            Graph graph = new Graph(
                start.Compilation,
                GeneratedCode.For(start.Compilation, start.Options.AnalyzerConfigOptionsProvider.GlobalOptions));

            start.RegisterSymbolAction(graph.CollectDeclaration, SymbolKind.NamedType);
            start.RegisterOperationAction(
                graph.CollectOperation,
                OperationKind.ObjectCreation,
                OperationKind.Invocation,
                OperationKind.FieldReference,
                OperationKind.PropertyReference,
                OperationKind.MethodReference,
                OperationKind.EventReference,
                OperationKind.TypeOf,
                OperationKind.NameOf,
                OperationKind.IsType,
                OperationKind.DeclarationPattern,
                OperationKind.TypePattern,
                OperationKind.RecursivePattern,
                OperationKind.Conversion,
                OperationKind.VariableDeclarator,
                OperationKind.DefaultValue,
                OperationKind.ArrayCreation,
                OperationKind.DelegateCreation,
                OperationKind.AnonymousFunction,
                OperationKind.LocalFunction,
                OperationKind.SizeOf,
                OperationKind.CatchClause,
                OperationKind.Tuple,
                OperationKind.Literal);
            start.RegisterCompilationEndAction(graph.Report);
        });
    }

    /// <summary>
    /// The namespace graph of one compilation, filled concurrently and read once at the end.
    /// </summary>
    private sealed class Graph
    {
        private readonly Compilation compilation;
        private readonly GeneratedCode generated;

        /// <summary>Each edge with the first location that made it: the lowest path, then the lowest span.</summary>
        private readonly ConcurrentDictionary<Edge, Location> edges = new ConcurrentDictionary<Edge, Location>();

        internal Graph(Compilation compilation, GeneratedCode generated)
        {
            this.compilation = compilation;
            this.generated = generated;
        }

        /// <summary>What a type's declaration names: bases, attributes, constraints, member signatures.</summary>
        internal void CollectDeclaration(SymbolAnalysisContext context)
        {
            INamedTypeSymbol type = (INamedTypeSymbol)context.Symbol;

            if (type.IsImplicitlyDeclared || Owner(type) is not { } owner || !DeclaredByHand(type))
            {
                return;
            }

            Location at = type.Locations.Length > 0 ? type.Locations[0] : Location.None;

            Add(owner, type.BaseType, at);
            foreach (INamedTypeSymbol @interface in type.Interfaces)
            {
                Add(owner, @interface, at);
            }

            AddAttributes(owner, type, at);
            AddConstraints(owner, type.TypeParameters, at);

            if (type.TypeKind == TypeKind.Delegate && type.DelegateInvokeMethod is { } invoke)
            {
                AddSignature(owner, invoke, at);
            }

            foreach (ISymbol member in type.GetMembers())
            {
                // Nested types are their own symbols and arrive on their own; the compiler's own
                // members (a record's equality, a property's accessors) name nothing their
                // declaration did not.
                if (member is INamedTypeSymbol || member.IsImplicitlyDeclared || !DeclaredByHand(member))
                {
                    continue;
                }

                Location memberAt = member.Locations.Length > 0 ? member.Locations[0] : at;
                AddAttributes(owner, member, memberAt);

                switch (member)
                {
                    case IMethodSymbol method:
                        if (method.MethodKind is MethodKind.PropertyGet or MethodKind.PropertySet
                            or MethodKind.EventAdd or MethodKind.EventRemove or MethodKind.EventRaise)
                        {
                            break;
                        }

                        AddSignature(owner, method, memberAt);
                        AddConstraints(owner, method.TypeParameters, memberAt);
                        break;
                    case IPropertySymbol property:
                        Add(owner, property.Type, memberAt);
                        foreach (IParameterSymbol parameter in property.Parameters)
                        {
                            Add(owner, parameter.Type, memberAt);
                        }

                        break;
                    case IFieldSymbol field:
                        Add(owner, field.Type, memberAt);
                        break;
                    case IEventSymbol @event:
                        Add(owner, @event.Type, memberAt);
                        break;
                }
            }
        }

        /// <summary>What a body or initialiser binds.</summary>
        internal void CollectOperation(OperationAnalysisContext context)
        {
            if (Owner(context.ContainingSymbol) is not { } owner)
            {
                return;
            }

            IOperation operation = context.Operation;
            Location at = operation.Syntax.GetLocation();

            Add(owner, operation.Type, at);

            switch (operation)
            {
                case IObjectCreationOperation creation:
                    Add(owner, creation.Constructor?.ContainingType, at);
                    break;
                case IInvocationOperation invocation:
                    // The containing type covers a static call and an extension method's class;
                    // the type arguments cover what a generic call was closed over.
                    Add(owner, invocation.TargetMethod.ContainingType, at);
                    foreach (ITypeSymbol argument in invocation.TargetMethod.TypeArguments)
                    {
                        Add(owner, argument, at);
                    }

                    break;
                case IMemberReferenceOperation reference:
                    Add(owner, reference.Member.ContainingType, at);
                    if (reference is IMethodReferenceOperation { Method: { } group })
                    {
                        foreach (ITypeSymbol argument in group.TypeArguments)
                        {
                            Add(owner, argument, at);
                        }
                    }

                    break;
                case ITypeOfOperation typeOf:
                    Add(owner, typeOf.TypeOperand, at);
                    break;
                case ISizeOfOperation sizeOf:
                    Add(owner, sizeOf.TypeOperand, at);
                    break;
                case INameOfOperation nameOf:
                    // nameof(Other.Thing) binds its argument but is a constant string, so the
                    // argument carries no type; the name it binds to is asked for directly.
                    AddBound(owner, nameOf.Argument, at);
                    break;
                case IIsTypeOperation isType:
                    Add(owner, isType.TypeOperand, at);
                    break;
                case IDeclarationPatternOperation declaration:
                    Add(owner, declaration.MatchedType, at);
                    break;
                case ITypePatternOperation typePattern:
                    Add(owner, typePattern.MatchedType, at);
                    break;
                case IRecursivePatternOperation recursive:
                    Add(owner, recursive.MatchedType, at);
                    break;
                case IVariableDeclaratorOperation declarator:
                    Add(owner, declarator.Symbol.Type, at);
                    break;
                case IAnonymousFunctionOperation lambda:
                    AddSignature(owner, lambda.Symbol, at);
                    break;
                case ILocalFunctionOperation local:
                    AddSignature(owner, local.Symbol, at);
                    break;
                case ICatchClauseOperation @catch:
                    Add(owner, @catch.ExceptionType, at);
                    break;
            }
        }

        /// <summary>Strongly connected components, then one diagnostic per edge inside a non-trivial one.</summary>
        internal void Report(CompilationAnalysisContext context)
        {
            if (edges.IsEmpty)
            {
                return;
            }

            // Deterministic: nodes by name, and each node's successors by name.
            SortedDictionary<string, SortedSet<string>> successors = new SortedDictionary<string, SortedSet<string>>(StringComparer.Ordinal);
            foreach (Edge edge in edges.Keys)
            {
                Successors(successors, edge.From).Add(edge.To);
                Successors(successors, edge.To);
            }

            Dictionary<string, int> component = StronglyConnectedComponents(successors);

            List<(Edge Edge, Location At)> onCycles = new List<(Edge, Location)>();
            foreach (KeyValuePair<Edge, Location> edge in edges)
            {
                if (component[edge.Key.From] == component[edge.Key.To])
                {
                    onCycles.Add((edge.Key, edge.Value));
                }
            }

            foreach ((Edge edge, Location at) in onCycles.OrderBy(static e => e.At, LocationOrder.Instance))
            {
                string wayBack = string.Join(" -> ", ShortestPath(successors, component, edge.To, edge.From));

                context.ReportDiagnostic(Diagnostic.Create(
                    Descriptors.NamespaceCycle,
                    at,
                    $"namespace '{edge.From}' refers to '{edge.To}' here, and the reference comes back: {wayBack}",
                    DesignChange));
            }
        }

        /// <summary>
        /// The namespace a symbol's declaration belongs to - that of its outermost containing type -
        /// or null when the symbol is not source, is generated, or is answered by
        /// <c>[DesignDecision]</c> on it or on a type containing it.
        /// </summary>
        private string? Owner(ISymbol? symbol)
        {
            INamedTypeSymbol? outermost = null;

            for (ISymbol? current = symbol; current is not null and not INamespaceSymbol; current = current.ContainingSymbol)
            {
                if (Markers.Has(current, Markers.DesignDecision))
                {
                    return null;
                }

                if (current is INamedTypeSymbol type)
                {
                    outermost = type;
                }
            }

            return outermost is not null && IsSourceHere(outermost) ? NamespaceOf(outermost) : null;
        }

        private void Add(string owner, ITypeSymbol? type, Location at)
        {
            switch (type)
            {
                case null:
                    return;
                case IArrayTypeSymbol array:
                    Add(owner, array.ElementType, at);
                    return;
                case IPointerTypeSymbol pointer:
                    Add(owner, pointer.PointedAtType, at);
                    return;
                case INamedTypeSymbol named:
                    if (named.IsTupleType)
                    {
                        foreach (IFieldSymbol element in named.TupleElements)
                        {
                            Add(owner, element.Type, at);
                        }

                        return;
                    }

                    foreach (ITypeSymbol argument in named.TypeArguments)
                    {
                        Add(owner, argument, at);
                    }

                    INamedTypeSymbol definition = named.OriginalDefinition;
                    if (!IsSourceHere(definition))
                    {
                        return;
                    }

                    string target = NamespaceOf(definition);
                    if (target != owner)
                    {
                        edges.AddOrUpdate(
                            new Edge(owner, target),
                            at,
                            (_, existing) => LocationOrder.Instance.Compare(at, existing) < 0 ? at : existing);
                    }

                    return;
                default:
                    return;
            }
        }

        private void AddBound(string owner, IOperation argument, Location at)
        {
            if (argument.SemanticModel?.GetSymbolInfo(argument.Syntax).Symbol is { } symbol)
            {
                Add(owner, symbol as ITypeSymbol ?? symbol.ContainingType, at);
            }
        }

        private void AddSignature(string owner, IMethodSymbol method, Location at)
        {
            Add(owner, method.ReturnType, at);
            foreach (IParameterSymbol parameter in method.Parameters)
            {
                Add(owner, parameter.Type, at);
            }
        }

        private void AddAttributes(string owner, ISymbol symbol, Location at)
        {
            foreach (AttributeData attribute in symbol.GetAttributes())
            {
                Add(owner, attribute.AttributeClass, at);
            }
        }

        private void AddConstraints(string owner, ImmutableArray<ITypeParameterSymbol> parameters, Location at)
        {
            foreach (ITypeParameterSymbol parameter in parameters)
            {
                foreach (ITypeSymbol constraint in parameter.ConstraintTypes)
                {
                    Add(owner, constraint, at);
                }
            }
        }

        /// <summary>Declared in source in this compilation, and not only in generated code.</summary>
        private bool IsSourceHere(INamedTypeSymbol type) =>
            SymbolEqualityComparer.Default.Equals(type.ContainingAssembly, compilation.Assembly) && DeclaredByHand(type);

        /// <summary>
        /// At least one declaration outside generated code. A partial type's generated half, and the
        /// members declared in it, are the generator author's and make no edge.
        /// </summary>
        private bool DeclaredByHand(ISymbol symbol)
        {
            foreach (SyntaxReference reference in symbol.DeclaringSyntaxReferences)
            {
                if (!generated.Contains(reference.SyntaxTree))
                {
                    return true;
                }
            }

            return false;
        }

        private static string NamespaceOf(INamedTypeSymbol type)
        {
            INamedTypeSymbol outermost = type;
            while (outermost.ContainingType is { } containing)
            {
                outermost = containing;
            }

            return outermost.ContainingNamespace is { IsGlobalNamespace: false } ns ? ns.ToDisplayString() : string.Empty;
        }

        private static SortedSet<string> Successors(SortedDictionary<string, SortedSet<string>> graph, string node)
        {
            if (!graph.TryGetValue(node, out SortedSet<string>? set))
            {
                set = new SortedSet<string>(StringComparer.Ordinal);
                graph[node] = set;
            }

            return set;
        }

        /// <summary>Tarjan's algorithm, iterative so that a long chain of namespaces cannot overflow the stack.</summary>
        private static Dictionary<string, int> StronglyConnectedComponents(SortedDictionary<string, SortedSet<string>> graph)
        {
            Dictionary<string, int> index = new Dictionary<string, int>(StringComparer.Ordinal);
            Dictionary<string, int> low = new Dictionary<string, int>(StringComparer.Ordinal);
            Dictionary<string, int> component = new Dictionary<string, int>(StringComparer.Ordinal);
            HashSet<string> onStack = new HashSet<string>(StringComparer.Ordinal);
            Stack<string> stack = new Stack<string>();
            int next = 0;
            int components = 0;

            foreach (string root in graph.Keys)
            {
                if (index.ContainsKey(root))
                {
                    continue;
                }

                Stack<(string Node, IEnumerator<string> Remaining)> work = new Stack<(string, IEnumerator<string>)>();
                Visit(root);

                while (work.Count > 0)
                {
                    (string node, IEnumerator<string> remaining) = work.Peek();

                    if (remaining.MoveNext())
                    {
                        string successor = remaining.Current;
                        if (!index.ContainsKey(successor))
                        {
                            Visit(successor);
                        }
                        else if (onStack.Contains(successor))
                        {
                            low[node] = Math.Min(low[node], index[successor]);
                        }

                        continue;
                    }

                    work.Pop();

                    if (low[node] == index[node])
                    {
                        string member;
                        do
                        {
                            member = stack.Pop();
                            onStack.Remove(member);
                            component[member] = components;
                        }
                        while (member != node);

                        components++;
                    }

                    if (work.Count > 0)
                    {
                        string parent = work.Peek().Node;
                        low[parent] = Math.Min(low[parent], low[node]);
                    }
                }

                void Visit(string node)
                {
                    index[node] = next;
                    low[node] = next;
                    next++;
                    stack.Push(node);
                    onStack.Add(node);
                    work.Push((node, graph[node].GetEnumerator()));
                }
            }

            return component;
        }

        /// <summary>One shortest path from <paramref name="from"/> to <paramref name="to"/> inside their component, by breadth-first search.</summary>
        private static List<string> ShortestPath(
            SortedDictionary<string, SortedSet<string>> graph,
            Dictionary<string, int> component,
            string from,
            string to)
        {
            Dictionary<string, string> previous = new Dictionary<string, string>(StringComparer.Ordinal);
            Queue<string> queue = new Queue<string>();
            queue.Enqueue(from);
            previous[from] = from;

            while (queue.Count > 0)
            {
                string node = queue.Dequeue();
                if (node == to)
                {
                    break;
                }

                foreach (string successor in graph[node])
                {
                    if (component[successor] == component[from] && !previous.ContainsKey(successor))
                    {
                        previous[successor] = node;
                        queue.Enqueue(successor);
                    }
                }
            }

            List<string> path = new List<string>();
            for (string node = to; ; node = previous[node])
            {
                path.Add(node);
                if (node == from)
                {
                    break;
                }
            }

            path.Reverse();
            return path;
        }

        private readonly struct Edge : IEquatable<Edge>
        {
            internal Edge(string from, string to)
            {
                From = from;
                To = to;
            }

            internal string From { get; }

            internal string To { get; }

            public bool Equals(Edge other) => From == other.From && To == other.To;

            public override bool Equals(object? obj) => obj is Edge other && Equals(other);

            public override int GetHashCode() => unchecked((From.GetHashCode() * 397) ^ To.GetHashCode());
        }

        /// <summary>File path, then span start: the order a reader meets locations in.</summary>
        private sealed class LocationOrder : IComparer<Location>
        {
            internal static readonly LocationOrder Instance = new LocationOrder();

            public int Compare(Location? x, Location? y)
            {
                if (ReferenceEquals(x, y))
                {
                    return 0;
                }

                if (x is null || !x.IsInSource)
                {
                    return y is null || !y.IsInSource ? 0 : 1;
                }

                if (y is null || !y.IsInSource)
                {
                    return -1;
                }

                int byPath = string.CompareOrdinal(x.SourceTree!.FilePath, y.SourceTree!.FilePath);
                return byPath != 0 ? byPath : x.SourceSpan.Start.CompareTo(y.SourceSpan.Start);
            }
        }
    }
}
