using System;
using System.Collections.Generic;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Operations;

namespace DecisionDriven.Analyzers.Rules;

/// <summary>The three shapes a <c>[Memoized]</c> field may have.</summary>
internal enum MemoForm
{
    /// <summary>Not a memo DD0019 accepts; the field's shape is the finding.</summary>
    None,

    /// <summary>A private, non-readonly field of an immutable type, filled once.</summary>
    Value,

    /// <summary>A private readonly <c>ConcurrentDictionary&lt;K, V&gt;</c> filled through <c>GetOrAdd</c>.</summary>
    PerKey,

    /// <summary>
    /// A private, non-readonly <c>HashSet&lt;T&gt;</c> or <c>Dictionary&lt;K, V&gt;</c>, or a read-only
    /// interface over one, built in a local, published once and only read afterwards.
    /// </summary>
    Published,
}

/// <summary>
/// What DD0019 checks about a <c>[Memoized]</c> field instead of reporting the writes to it.
/// </summary>
/// <remarks>
/// <para>
/// <c>ImmutableModel.MemoizedFieldsAreVerified</c>. A cached hash, a lazily built immutable view or
/// a per-key cache of a computed answer is written after the value is handed out, and its observable
/// value never changes: every reader sees what the first one would have computed. DD0019 reported
/// them all as mutation, which left a consumer citing a decision for a pattern that is correct.
/// </para>
/// <para>
/// The attribute is a claim, not an exemption. The rule accepts exactly three forms, and anything it
/// cannot show is one of them stays a finding: the direction of error is the same as the rest of
/// DD0019's body check.
/// </para>
/// </remarks>
internal static class MemoizedFields
{
    private const string ConcurrentDictionary = "System.Collections.Concurrent.ConcurrentDictionary";

    private const string GetOrAdd = "GetOrAdd";

    /// <summary>The collections a published-once memo may be built as, by name without arity.</summary>
    private static readonly string[] Buildable =
    {
        "System.Collections.Generic.HashSet",
        "System.Collections.Generic.Dictionary",
    };

    /// <summary>What a published-once memo's field may be typed as: a buildable collection or its read-only interface.</summary>
    private static readonly string[] Publishable =
    {
        "System.Collections.Generic.HashSet",
        "System.Collections.Generic.Dictionary",
        "System.Collections.Generic.IReadOnlySet",
        "System.Collections.Generic.IReadOnlyDictionary",
    };

    private const string PlainCollectionDesignChange =
        "hold a readonly System.Collections.Concurrent.ConcurrentDictionary<K, V> filled only through GetOrAdd with a static factory, "
        + "build a HashSet<T> or Dictionary<K, V> in a local and publish it once to a field that is not readonly, "
        + "compute it in the constructor, or build it in a sealed *Builder";

    /// <summary>A shape finding, or the form the field takes.</summary>
    internal readonly struct Shape
    {
        internal Shape(MemoForm form, string? finding = null, string? designChange = null)
        {
            Form = form;
            Finding = finding;
            DesignChange = designChange;
        }

        internal MemoForm Form { get; }

        internal string? Finding { get; }

        internal string? DesignChange { get; }
    }

    /// <summary>One place a member of the type touches a memo field.</summary>
    internal sealed class Use
    {
        internal Use(IFieldSymbol field, ISymbol member, IOperation at)
        {
            Field = field;
            Member = member;
            At = at;
        }

        internal IFieldSymbol Field { get; }

        /// <summary>The member whose body this is: a lambda or local function belongs to its member.</summary>
        internal ISymbol Member { get; }

        internal IOperation At { get; }

        /// <summary>True for a write in one of the accepted forms: what makes a member the writer.</summary>
        internal bool Fills { get; set; }

        /// <summary>A finding that does not depend on the rest of the type, or null.</summary>
        internal string? Finding { get; set; }

        internal string? DesignChange { get; set; }

        /// <summary>The expressions the memo is computed from, each with the local copy it may read.</summary>
        internal List<(IOperation Value, ILocalSymbol? Copy)> Computed { get; } = new List<(IOperation, ILocalSymbol?)>();

        /// <summary>For a published-once memo, the publication of a fresh local this use is.</summary>
        internal Publication? Published { get; set; }
    }

    /// <summary>
    /// <c>Interlocked.CompareExchange(ref field, local, null)</c> or <c>field ??= local</c>, where the
    /// local holds a collection the member creates, or one a private helper of the type returns.
    /// </summary>
    internal sealed class Publication
    {
        internal Publication(ISymbol member, IOperation root, ILocalSymbol local, IOperation declarator, IOperation at, IMethodSymbol? helper)
        {
            Member = member;
            Root = root;
            Local = local;
            Declarator = declarator;
            At = at;
            Helper = helper;
        }

        internal ISymbol Member { get; }

        /// <summary>The operation block of the member the publication is in.</summary>
        internal IOperation Root { get; }

        internal ILocalSymbol Local { get; }

        internal IOperation Declarator { get; }

        /// <summary>The assignment or the call that publishes the local.</summary>
        internal IOperation At { get; }

        /// <summary>The private helper the local is built by, or null for a collection created here.</summary>
        internal IMethodSymbol? Helper { get; }
    }

    /// <summary>A finding on a published-once memo, where it is and what to do instead.</summary>
    internal readonly struct Finding
    {
        internal Finding(IOperation at, string text, string designChange)
        {
            At = at;
            Text = text;
            DesignChange = designChange;
        }

        internal IOperation At { get; }

        internal string Text { get; }

        internal string DesignChange { get; }
    }

    /// <summary>The form <paramref name="field"/> takes, or the finding that says why it takes neither.</summary>
    internal static Shape Classify(IFieldSymbol field, string owner)
    {
        string name = $"'{owner}.{field.Name}'";
        ITypeSymbol type = field.Type;

        if (field.IsStatic)
        {
            return new Shape(
                MemoForm.None,
                $"{name} is [Memoized] and static, so it is shared by every value rather than belonging to one",
                "memoise per instance, or compute it once in a static readonly initializer");
        }

        if (field.DeclaredAccessibility != Accessibility.Private)
        {
            return new Shape(
                MemoForm.None,
                $"{name} is [Memoized] and is not private, so code outside the type can write to it",
                "make it private");
        }

        if (type is INamedTypeSymbol { TypeArguments.Length: 2 } dictionary
            && string.Equals(Wrappers.Name(dictionary), ConcurrentDictionary, StringComparison.Ordinal))
        {
            if (!field.IsReadOnly)
            {
                return new Shape(
                    MemoForm.None,
                    $"{name} is a [Memoized] ConcurrentDictionary that is not readonly, so the dictionary itself can be replaced",
                    "make it readonly");
            }

            ITypeSymbol values = dictionary.TypeArguments[1];

            if (!IsImmutable(values))
            {
                return new Shape(
                    MemoForm.None,
                    $"{name} is [Memoized] and the values it holds, '{values.ToDisplayString(Display.Format)}', are mutable, "
                        + "so a value memoised for a key can change under whoever got it",
                    "memoise an immutable value per key: a primitive, string, enum or immutable type");
            }

            return new Shape(MemoForm.PerKey);
        }

        if (!field.IsReadOnly && type is INamedTypeSymbol published && IsNamed(published, Publishable))
        {
            // HashSet<T>, IReadOnlySet<T>: the elements. Dictionary<K, V>: the keys, then the values.
            string[] held = published.TypeArguments.Length == 1 ? new[] { "elements" } : new[] { "keys", "values" };

            for (int i = 0; i < published.TypeArguments.Length; i++)
            {
                ITypeSymbol argument = published.TypeArguments[i];

                if (!IsImmutable(argument))
                {
                    return new Shape(
                        MemoForm.None,
                        $"{name} is [Memoized] and the {held[i]} it holds, '{argument.ToDisplayString(Display.Format)}', are mutable, "
                            + "so what it holds can change under whoever reads it",
                        $"hold immutable {held[i]}: a primitive, string, enum or immutable type");
                }
            }

            return new Shape(MemoForm.Published);
        }

        if (IsPlainCollection(type))
        {
            return new Shape(
                MemoForm.None,
                $"{name} is [Memoized] and is a plain '{type.ToDisplayString(Display.Format)}', "
                    + "and a plain collection filled by concurrent readers is a race",
                PlainCollectionDesignChange);
        }

        if (!IsImmutable(type))
        {
            return new Shape(
                MemoForm.None,
                $"{name} is [Memoized] and its type, '{type.ToDisplayString(Display.Format)}', is mutable, "
                    + "so the value it caches can change under whoever reads it",
                "memoise a primitive, string, enum or immutable type, compute it in the constructor, "
                    + "or build it in a sealed *Builder");
        }

        if (field.IsReadOnly)
        {
            return new Shape(
                MemoForm.None,
                $"{name} is [Memoized] and readonly, so it is set in the constructor and there is nothing to memoise",
                "take [Memoized] off");
        }

        return new Shape(MemoForm.Value);
    }

    /// <summary>
    /// A type whose value cannot change once made: a primitive, string, enum, a type DD0004 judges
    /// immutable, or <c>Nullable&lt;T&gt;</c> of one.
    /// </summary>
    /// <remarks>
    /// A type parameter is not: whatever it is bound to decides, and the rule cannot see that here.
    /// </remarks>
    internal static bool IsImmutable(ITypeSymbol type)
    {
        if (type is ITypeParameterSymbol or IErrorTypeSymbol or IPointerTypeSymbol or IDynamicTypeSymbol)
        {
            return false;
        }

        if (type is INamedTypeSymbol { OriginalDefinition.SpecialType: SpecialType.System_Nullable_T, TypeArguments.Length: 1 } nullable)
        {
            return IsImmutable(nullable.TypeArguments[0]);
        }

        if (type.SpecialType != SpecialType.None)
        {
            return type.SpecialType != SpecialType.System_Object;
        }

        if (type.TypeKind == TypeKind.Enum)
        {
            return true;
        }

        return !MutableTypes.IsMutable(type);
    }

    /// <summary>An array, or a mutable collection from <c>System.Collections</c>.</summary>
    private static bool IsPlainCollection(ITypeSymbol type) =>
        type is IArrayTypeSymbol
        || (MutableTypes.IsMutable(type)
            && type.ContainingNamespace?.ToDisplayString() is "System.Collections" or "System.Collections.Generic" or "System.Collections.ObjectModel");

    /// <summary>
    /// Every use of a memo field in one member's body, classified as far as the body alone says.
    /// </summary>
    /// <param name="root">One operation block of the member.</param>
    /// <param name="member">The member the block belongs to.</param>
    /// <param name="owner">The type, as findings name it.</param>
    /// <param name="formOf">The form of a field of the type, <see cref="MemoForm.None"/> for any other field.</param>
    /// <param name="into">Where each use goes.</param>
    internal static void Scan(IOperation root, ISymbol member, string owner, Func<IFieldSymbol, MemoForm> formOf, Action<Use> into)
    {
        // var h = field; — a local copy of a value memo, which the guard may test instead of the field.
        Dictionary<ILocalSymbol, IFieldSymbol> copies = new Dictionary<ILocalSymbol, IFieldSymbol>(SymbolEqualityComparer.Default);

        foreach (IOperation operation in root.DescendantsAndSelf())
        {
            if (operation is IVariableDeclaratorOperation { Symbol: { RefKind: RefKind.None } local, Initializer.Value: { } initial }
                && Strip(initial) is IFieldReferenceOperation { Field: { } copied } copiedFrom
                && IsThis(copiedFrom.Instance)
                && formOf(copied.OriginalDefinition) == MemoForm.Value)
            {
                copies[local] = copied.OriginalDefinition;
            }
        }

        foreach (IOperation operation in root.DescendantsAndSelf())
        {
            if (operation is not IFieldReferenceOperation { Field: { } field } reference)
            {
                continue;
            }

            switch (formOf(field.OriginalDefinition))
            {
                case MemoForm.Value:
                    into(ValueUse(root, member, owner, field.OriginalDefinition, reference, copies));
                    break;

                case MemoForm.PerKey:
                    into(PerKeyUse(member, owner, field.OriginalDefinition, reference));
                    break;

                case MemoForm.Published:
                    into(PublishedUse(root, member, owner, field.OriginalDefinition, reference));
                    break;
            }
        }
    }

    private static Use ValueUse(
        IOperation root,
        ISymbol member,
        string owner,
        IFieldSymbol field,
        IFieldReferenceOperation reference,
        Dictionary<ILocalSymbol, IFieldSymbol> copies)
    {
        Use use = new Use(field, member, reference);

        switch (reference.Parent)
        {
            case ICoalesceAssignmentOperation coalesce when coalesce.Target == reference && IsThis(reference.Instance):
                use.Fills = true;
                use.Computed.Add((coalesce.Value, null));
                return use;

            case ISimpleAssignmentOperation { IsRef: false } assignment when assignment.Target == reference
                && IsThis(reference.Instance)
                && IsGuarded(assignment, field, copies, out ILocalSymbol? copy):
                use.Fills = true;
                use.Computed.Add((assignment.Value, copy));

                if (copy is not null)
                {
                    // var h = hash; if (h == 0) { h = Compute(); hash = h; }: what h is given is the computation.
                    foreach (IOperation operation in root.DescendantsAndSelf())
                    {
                        if (operation is ISimpleAssignmentOperation { Target: ILocalReferenceOperation { Local: { } target } } toCopy
                            && SymbolEqualityComparer.Default.Equals(target, copy))
                        {
                            use.Computed.Add((toCopy.Value, copy));
                        }
                    }
                }

                return use;
        }

        if (IsWritten(reference, member))
        {
            string name = $"'{field.Name}'";
            use.Finding = $"'{owner}.{MemberName(member)}' writes [Memoized] {name} other than by "
                + $"'{field.Name} ??= ...' or 'if ({field.Name} == sentinel) {field.Name} = ...', "
                + "so the value can change after it was first read";
            use.DesignChange = "write it only in one of those two forms, or take [Memoized] off and compute it in the constructor";
        }

        return use;
    }

    private static Use PerKeyUse(ISymbol member, string owner, IFieldSymbol field, IFieldReferenceOperation reference)
    {
        Use use = new Use(field, member, reference);
        string at = $"'{owner}.{MemberName(member)}'";

        switch (reference.Parent)
        {
            case IInvocationOperation invocation when invocation.Instance == reference:
                IMethodSymbol called = invocation.TargetMethod;

                if (string.Equals(called.Name, GetOrAdd, StringComparison.Ordinal)
                    && called.Parameters.Length >= 2
                    && called.Parameters[1].Type.TypeKind == TypeKind.Delegate)
                {
                    use.Fills = true;

                    foreach (IArgumentOperation argument in invocation.Arguments)
                    {
                        if (argument.Parameter?.Ordinal == 1)
                        {
                            if (!IsStaticFactory(argument.Value))
                            {
                                use.Finding = $"{at} passes '{field.Name}.GetOrAdd' a factory that is not a static lambda or a static method, "
                                    + "so it can capture state and what is memoised for a key depends on who asked first";
                                use.DesignChange = "make the factory a static lambda or a static method, and pass what it needs as GetOrAdd's third argument";
                            }
                        }
                        else
                        {
                            use.Computed.Add((argument.Value, null));
                        }
                    }

                    return use;
                }

                if (IsRead(called, field.Type))
                {
                    return use;
                }

                return OtherCall(use, at, field, called.Name == GetOrAdd ? "GetOrAdd(key, value)" : called.Name);

            case IMethodReferenceOperation method when method.Instance == reference:
                return IsRead(method.Method, field.Type) ? use : OtherCall(use, at, field, method.Method.Name);

            case IPropertyReferenceOperation property when property.Instance == reference:
                return IsWritten(property, member)
                    ? OtherCall(use, at, field, property.Property.IsIndexer ? "this[]" : property.Property.Name)
                    : use;

            case ISimpleAssignmentOperation { IsRef: false } made when made.Target == reference
                && member is IMethodSymbol { MethodKind: MethodKind.Constructor }:
                // Readonly: the compiler allows this in a constructor and nowhere else.
                return use;

            case IForEachLoopOperation:
            case IConversionOperation { Parent: IForEachLoopOperation }:
                return use;
        }

        if (IsNullTest(reference))
        {
            return use;
        }

        use.Finding = $"{at} hands out the [Memoized] '{field.Name}' itself, and whoever gets it can write to it";
        use.DesignChange = "return what GetOrAdd or a read returns, not the dictionary";
        return use;
    }

    /// <summary>
    /// <c>field is null</c>, <c>field is not null</c>, <c>field == null</c> or <c>field != null</c>:
    /// a comparison of the reference, which reads the field and gives nothing out (#118).
    /// </summary>
    private static bool IsNullTest(IOperation reference)
    {
        IOperation tested = reference;

        // field == null compares as object: the field arrives through an implicit conversion.
        while (tested.Parent is IConversionOperation { IsImplicit: true } conversion)
        {
            tested = conversion;
        }

        return tested.Parent switch
        {
            IIsPatternOperation pattern => pattern.Value == tested && IsNullPattern(pattern.Pattern),
            IBinaryOperation { OperatorKind: BinaryOperatorKind.Equals or BinaryOperatorKind.NotEquals } comparison =>
                (comparison.LeftOperand == tested && IsNull(comparison.RightOperand))
                || (comparison.RightOperand == tested && IsNull(comparison.LeftOperand)),
            _ => false,
        };
    }

    private static bool IsNullPattern(IPatternOperation pattern) => pattern switch
    {
        IConstantPatternOperation constant => IsNull(constant.Value),
        INegatedPatternOperation negated => IsNullPattern(negated.Pattern),
        _ => false,
    };

    private static bool IsNull(IOperation operation)
    {
        IOperation value = Strip(operation);
        return value.ConstantValue is { HasValue: true, Value: null };
    }

    private static Use PublishedUse(IOperation root, ISymbol member, string owner, IFieldSymbol field, IFieldReferenceOperation reference)
    {
        Use use = new Use(field, member, reference);
        string at = $"'{owner}.{MemberName(member)}'";

        switch (reference.Parent)
        {
            case ICoalesceAssignmentOperation coalesce when coalesce.Target == reference && IsThis(reference.Instance):
                return Publish(use, root, member, at, field, coalesce, coalesce.Value);

            case IArgumentOperation { Parameter: { Ordinal: 0, RefKind: RefKind.Ref } } location
                when location.Parent is IInvocationOperation exchange
                    && IsCompareExchange(exchange.TargetMethod)
                    && IsThis(reference.Instance):
                IOperation? value = null;
                IOperation? comparand = null;

                foreach (IArgumentOperation argument in exchange.Arguments)
                {
                    switch (argument.Parameter?.Ordinal)
                    {
                        case 1:
                            value = argument.Value;
                            break;
                        case 2:
                            comparand = argument.Value;
                            break;
                    }
                }

                return value is not null && comparand is not null && IsNull(comparand)
                    ? Publish(use, root, member, at, field, exchange, value)
                    : Store(use, at, field);

            case IInvocationOperation invocation when invocation.Instance == reference:
                return IsCollectionRead(invocation.TargetMethod, field.Type)
                    ? use
                    : WriteAfter(use, at, field, $"{field.Name}.{invocation.TargetMethod.Name}");

            case IMethodReferenceOperation method when method.Instance == reference:
                return IsCollectionRead(method.Method, field.Type)
                    ? use
                    : WriteAfter(use, at, field, $"{field.Name}.{method.Method.Name}");

            case IPropertyReferenceOperation property when property.Instance == reference:
                return IsWritten(property, member)
                    ? WriteAfter(use, at, field, $"{field.Name}.{(property.Property.IsIndexer ? "this[]" : property.Property.Name)}")
                    : use;

            case IForEachLoopOperation:
            case IConversionOperation { Parent: IForEachLoopOperation }:
                return use;

            // field?.Contains(x), field?.Count: the same call, made when the field is set.
            case IConditionalAccessOperation conditional when conditional.Operation == reference:
                switch (conditional.WhenNotNull)
                {
                    case IInvocationOperation { Instance: IConditionalAccessInstanceOperation } invocation:
                        return IsCollectionRead(invocation.TargetMethod, field.Type)
                            ? use
                            : WriteAfter(use, at, field, $"{field.Name}.{invocation.TargetMethod.Name}");

                    case IPropertyReferenceOperation { Instance: IConditionalAccessInstanceOperation } property:
                        return IsWritten(property, member)
                            ? WriteAfter(use, at, field, $"{field.Name}.{(property.Property.IsIndexer ? "this[]" : property.Property.Name)}")
                            : use;
                }

                break;
        }

        if (IsWritten(reference, member))
        {
            return Store(use, at, field);
        }

        return IsNullTest(reference) ? use : HandOut(use, at, field);
    }

    /// <summary>
    /// A publication, if <paramref name="value"/> is a local of the member holding a collection the
    /// member creates, or one a private helper of the type returns; otherwise the store finding.
    /// </summary>
    private static Use Publish(Use use, IOperation root, ISymbol member, string at, IFieldSymbol field, IOperation publication, IOperation value)
    {
        if (Strip(value) is not ILocalReferenceOperation { Local: { RefKind: RefKind.None } local }
            || Declarator(root, local) is not { Initializer.Value: { } initial } declarator)
        {
            return Store(use, at, field);
        }

        IMethodSymbol? helper = null;

        switch (Strip(initial))
        {
            case IObjectCreationOperation creation when IsFresh(creation):
                break;

            case IInvocationOperation { TargetMethod: { DeclaredAccessibility: Accessibility.Private, MethodKind: MethodKind.Ordinary } called } call
                when SymbolEqualityComparer.Default.Equals(called.ContainingType, member.ContainingType)
                    && (called.IsStatic || IsThis(call.Instance)):
                helper = called.OriginalDefinition;
                break;

            default:
                return Store(use, at, field);
        }

        use.Fills = true;
        use.Published = new Publication(member, root, local, declarator, publication, helper);

        // The publication's own result is the field, or what it held: a statement gives it to nobody.
        if (publication.Parent is not IExpressionStatementOperation)
        {
            return HandOut(use, at, field);
        }

        return use;
    }

    /// <summary>
    /// Everything about a publication that needs the rest of the type: the local's uses, what it is
    /// built from, and the helper that builds it.
    /// </summary>
    /// <param name="publication">The publication.</param>
    /// <param name="owner">The type, as findings name it.</param>
    /// <param name="field">The memo.</param>
    /// <param name="type">The type the memo belongs to.</param>
    /// <param name="changesNothing">Whether a method of the type changes nothing, as the body check sees it.</param>
    /// <param name="bodyOf">A private method's body, or null.</param>
    /// <param name="helpersSeen">Helpers already verified, so that one shared by two publications is reported once.</param>
    internal static List<Finding> Verify(
        Publication publication,
        string owner,
        IFieldSymbol field,
        INamedTypeSymbol type,
        Func<IMethodSymbol, bool> changesNothing,
        Func<IMethodSymbol, IOperation?> bodyOf,
        HashSet<IMethodSymbol> helpersSeen)
    {
        List<Finding> findings = new List<Finding>();
        string at = $"'{owner}.{MemberName(publication.Member)}'";

        LocalUses(publication.Root, publication.Local, publication.At, publication.Member, at, field, findings);

        IOperation[] region = Region(publication.Declarator, publication.At) ?? new[] { publication.Root };

        if (BuildImpurity(region, publication.Local, publication.At, type, changesNothing) is { } impure)
        {
            findings.Add(Computes(impure.At, at, field, impure.Reason));
        }

        if (publication.Helper is not { } helper)
        {
            return findings;
        }

        IOperation? body = bodyOf(helper);

        if (body is null || !ReturnsFresh(body, out ILocalSymbol? returned))
        {
            findings.Add(new Finding(
                publication.At,
                StoreFinding(at, field),
                StoreDesignChange(field)));
            return findings;
        }

        if (!helpersSeen.Add(helper))
        {
            return findings;
        }

        string inHelper = $"'{owner}.{helper.Name}'";

        if (returned is not null)
        {
            LocalUses(body, returned, null, helper, inHelper, field, findings);
        }

        if (BuildImpurity(new[] { body }, returned, null, type, changesNothing) is { } helperImpure)
        {
            findings.Add(Computes(helperImpure.At, inHelper, field, helperImpure.Reason));
        }

        return findings;
    }

    /// <summary>
    /// The uses of the local a collection is built in. Before the publication, only the collection's
    /// own members; after it, only its reads. In a helper, its returns are the publication.
    /// </summary>
    private static void LocalUses(IOperation root, ILocalSymbol local, IOperation? publication, ISymbol member, string at, IFieldSymbol field, List<Finding> findings)
    {
        foreach (IOperation operation in root.Descendants())
        {
            if (operation is not ILocalReferenceOperation reference
                || !SymbolEqualityComparer.Default.Equals(reference.Local, local))
            {
                continue;
            }

            if (publication is not null && Within(reference, publication))
            {
                continue;
            }

            IOperation node = reference;

            while (node.Parent is IConversionOperation { IsImplicit: true } conversion)
            {
                node = conversion;
            }

            bool after = publication is not null && reference.Syntax.SpanStart >= publication.Syntax.Span.End;

            if (!InFunction(reference, root))
            {
                switch (node.Parent)
                {
                    case IInvocationOperation invocation when invocation.Instance == node && IsOwnMember(invocation.TargetMethod):
                        if (after && !IsCollectionRead(invocation.TargetMethod, local.Type))
                        {
                            findings.Add(WriteAfterFinding(reference, at, field, $"{local.Name}.{invocation.TargetMethod.Name}"));
                        }

                        continue;

                    case IPropertyReferenceOperation property when property.Instance == node:
                        if (after && IsWritten(property, member))
                        {
                            findings.Add(WriteAfterFinding(reference, at, field, $"{local.Name}.{(property.Property.IsIndexer ? "this[]" : property.Property.Name)}"));
                        }

                        continue;

                    case IForEachLoopOperation loop when loop.Collection == node:
                        continue;

                    case IReturnOperation when publication is null:
                        continue;
                }

                if (IsNullTest(reference))
                {
                    continue;
                }
            }

            findings.Add(after
                ? new Finding(reference, HandOutFinding(at, field), HandOutDesignChange)
                : new Finding(
                    reference,
                    $"{at} lets '{local.Name}', the collection it publishes to [Memoized] '{field.Name}', out before publishing it, "
                        + "so whoever has it can write to it after it is published",
                    "fill the local only through its own members, such as Add and the indexer, and use it nowhere else until it is published"));
        }
    }

    /// <summary>
    /// The statements a collection is built in: from the local's declaration to the statement that
    /// publishes it, in the block that declares it. Null when they are not in one block.
    /// </summary>
    private static IOperation[]? Region(IOperation declarator, IOperation publication)
    {
        IOperation? statement = declarator;

        while (statement is not null and not IVariableDeclarationGroupOperation)
        {
            statement = statement.Parent;
        }

        if (statement?.Parent is not IBlockOperation block)
        {
            return null;
        }

        IOperation? last = publication;

        while (last is not null && last.Parent != block)
        {
            last = last.Parent;
        }

        if (last is null)
        {
            return null;
        }

        int from = -1;
        int to = -1;

        for (int i = 0; i < block.Operations.Length; i++)
        {
            if (block.Operations[i] == statement)
            {
                from = i;
            }

            if (block.Operations[i] == last)
            {
                to = i;
            }
        }

        if (from < 0 || to < from)
        {
            return null;
        }

        IOperation[] region = new IOperation[to - from + 1];

        for (int i = from; i <= to; i++)
        {
            region[i - from] = block.Operations[i];
        }

        return region;
    }

    /// <summary>
    /// The first operation of a build the value memo's expression rule refuses, and why. Filling the
    /// local through its own members, and an object or collection initializer filling the new
    /// collection, are the build itself; the publication is the store.
    /// </summary>
    private static (IOperation At, string Reason)? BuildImpurity(
        IOperation[] region,
        ILocalSymbol? local,
        IOperation? publication,
        INamedTypeSymbol type,
        Func<IMethodSymbol, bool> changesNothing)
    {
        int start = region[0].Syntax.SpanStart;
        int end = region[region.Length - 1].Syntax.Span.End;
        SyntaxTree tree = region[0].Syntax.SyntaxTree;

        bool Own(IOperation operation)
        {
            if (Strip(operation) is not ILocalReferenceOperation { Local: { } declared })
            {
                return false;
            }

            foreach (SyntaxReference declaration in declared.DeclaringSyntaxReferences)
            {
                if (declaration.SyntaxTree == tree && declaration.Span.Start >= start && declaration.Span.End <= end)
                {
                    return true;
                }
            }

            return false;
        }

        bool Builds(IOperation? instance) =>
            instance is not null
            && (Strip(instance) is IInstanceReferenceOperation { ReferenceKind: InstanceReferenceKind.ImplicitReceiver }
                || (local is not null && Strip(instance) is ILocalReferenceOperation { Local: { } on } && SymbolEqualityComparer.Default.Equals(on, local)));

        Stack<IOperation> pending = new Stack<IOperation>();

        for (int i = region.Length - 1; i >= 0; i--)
        {
            pending.Push(region[i]);
        }

        while (pending.Count > 0)
        {
            IOperation operation = pending.Pop();

            if (operation == publication)
            {
                continue;
            }

            bool build = operation switch
            {
                IInvocationOperation invocation => Builds(invocation.Instance),
                IMethodReferenceOperation method => Builds(method.Instance),
                IAssignmentOperation { Target: IPropertyReferenceOperation property } => Builds(property.Instance),
                _ => false,
            };

            if (!build && Reason(operation, Own, type, changesNothing) is { } reason)
            {
                return (operation, reason);
            }

            List<IOperation> children = new List<IOperation>(operation.ChildOperations);

            for (int i = children.Count - 1; i >= 0; i--)
            {
                pending.Push(children[i]);
            }
        }

        return null;
    }

    /// <summary>
    /// Whether every return of a helper's body gives back a collection it creates: a new
    /// <c>HashSet&lt;T&gt;</c> or <c>Dictionary&lt;K, V&gt;</c>, or one local holding one.
    /// </summary>
    private static bool ReturnsFresh(IOperation body, out ILocalSymbol? returned)
    {
        returned = null;
        bool any = false;

        foreach (IOperation operation in body.Descendants())
        {
            if (operation is not IReturnOperation { ReturnedValue: { } value } || InFunction(operation, body))
            {
                continue;
            }

            any = true;

            switch (Strip(value))
            {
                case IObjectCreationOperation creation when IsFresh(creation):
                    continue;

                case ILocalReferenceOperation { Local: { RefKind: RefKind.None } local }
                    when (returned is null || SymbolEqualityComparer.Default.Equals(returned, local))
                        && Declarator(body, local) is { Initializer.Value: { } initial }
                        && Strip(initial) is IObjectCreationOperation created
                        && IsFresh(created):
                    returned = local;
                    continue;

                default:
                    return false;
            }
        }

        return any;
    }

    private static IVariableDeclaratorOperation? Declarator(IOperation root, ILocalSymbol local)
    {
        foreach (IOperation operation in root.Descendants())
        {
            if (operation is IVariableDeclaratorOperation declarator && SymbolEqualityComparer.Default.Equals(declarator.Symbol, local))
            {
                return declarator;
            }
        }

        return null;
    }

    private static bool IsFresh(IObjectCreationOperation creation) =>
        creation.Type is INamedTypeSymbol created && IsNamed(created, Buildable);

    private static bool IsNamed(INamedTypeSymbol type, string[] names)
    {
        string name = Wrappers.Name(type);

        foreach (string candidate in names)
        {
            if (string.Equals(name, candidate, StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }

    private static bool IsCompareExchange(IMethodSymbol method) =>
        string.Equals(method.Name, "CompareExchange", StringComparison.Ordinal)
        && method.ContainingType is { } declaring
        && string.Equals(Wrappers.Name(declaring), "System.Threading.Interlocked", StringComparison.Ordinal);

    /// <summary>An instance member of the collection itself: how the local may be filled.</summary>
    private static bool IsOwnMember(IMethodSymbol method) =>
        !method.IsStatic
        && method.MethodKind != MethodKind.ReducedExtension
        && (method.ContainingType?.SpecialType == SpecialType.System_Object
            || method.ContainingType?.ContainingNamespace?.ToDisplayString() == "System.Collections.Generic");

    private static bool IsCollectionRead(IMethodSymbol called, ITypeSymbol held) =>
        HeldCollectionReads.IsRead(called, held) || called.ContainingType?.SpecialType == SpecialType.System_Object;

    /// <summary>Whether <paramref name="operation"/> is <paramref name="ancestor"/> or inside it.</summary>
    private static bool Within(IOperation operation, IOperation ancestor)
    {
        for (IOperation? current = operation; current is not null; current = current.Parent)
        {
            if (current == ancestor)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>Inside a lambda or local function of <paramref name="root"/>, which may run at any time.</summary>
    private static bool InFunction(IOperation operation, IOperation root)
    {
        for (IOperation? current = operation.Parent; current is not null && current != root; current = current.Parent)
        {
            if (current is IAnonymousFunctionOperation or ILocalFunctionOperation)
            {
                return true;
            }
        }

        return false;
    }

    private static Finding Computes(IOperation at, string member, IFieldSymbol field, string reason) =>
        new Finding(
            at,
            $"{member} computes [Memoized] '{field.Name}' from {reason}, so what is memoised depends on when it was first asked for",
            ComputesDesignChange);

    /// <summary>The design change for a computation outside the expression rule, for every form.</summary>
    internal const string ComputesDesignChange =
        "compute it only from readonly fields, other [Memoized] fields, parameters and calls that change nothing, or compute it in the constructor";

    private static Use Store(Use use, string at, IFieldSymbol field)
    {
        use.Finding = StoreFinding(at, field);
        use.DesignChange = StoreDesignChange(field);
        return use;
    }

    private static string StoreFinding(string at, IFieldSymbol field) =>
        $"{at} stores into [Memoized] '{field.Name}' something other than a collection it has just built in a local, "
            + "so readers can share a collection that is still changing";

    private static string StoreDesignChange(IFieldSymbol field) =>
        $"build a new HashSet<T> or Dictionary<K, V> in a local and publish it once, by "
            + $"'Interlocked.CompareExchange(ref {field.Name}, local, null)' or '{field.Name} ??= local'";

    private static Use WriteAfter(Use use, string at, IFieldSymbol field, string through)
    {
        Finding finding = WriteAfterFinding(use.At, at, field, through);
        use.Finding = finding.Text;
        use.DesignChange = finding.DesignChange;
        return use;
    }

    private static Finding WriteAfterFinding(IOperation where, string at, IFieldSymbol field, string through) =>
        new Finding(
            where,
            $"{at} writes to [Memoized] '{field.Name}' through '{through}' after it is published, so a reader can see it change",
            "finish filling the local before publishing it, and after that use only the collection's reads");

    private static Use HandOut(Use use, string at, IFieldSymbol field)
    {
        use.Finding = HandOutFinding(at, field);
        use.DesignChange = HandOutDesignChange;
        return use;
    }

    private static string HandOutFinding(string at, IFieldSymbol field) =>
        $"{at} hands out the [Memoized] '{field.Name}' itself, and whoever gets it can write to it";

    private const string HandOutDesignChange =
        "use only its reads, such as Contains, TryGetValue, Count and the indexer get, and return what they return, not the collection";

    private static Use OtherCall(Use use, string at, IFieldSymbol field, string called)
    {
        use.Finding = $"{at} calls '{field.Name}.{called}' on the [Memoized] '{field.Type.ToDisplayString(Display.Format)}', "
            + "which is neither GetOrAdd with a static factory nor a read, so a value memoised for a key can be replaced";
        use.DesignChange = "fill it only through GetOrAdd(key, static factory) or GetOrAdd(key, static factory, arg)";
        return use;
    }

    /// <summary>A read on the dictionary: the read-only interfaces it implements, or its own snapshot.</summary>
    private static bool IsRead(IMethodSymbol called, ITypeSymbol held) =>
        HeldCollectionReads.IsRead(called, held)
        || called.ContainingType?.SpecialType == SpecialType.System_Object
        || (string.Equals(called.Name, "ToArray", StringComparison.Ordinal)
            && called.ContainingType is { } declaring
            && string.Equals(Wrappers.Name(declaring), ConcurrentDictionary, StringComparison.Ordinal));

    /// <summary>
    /// <c>if (field == sentinel) field = ...;</c>, or the same testing a local copy of the field.
    /// The assignment is the statement the <c>if</c> runs, or one of the statements of its block.
    /// </summary>
    private static bool IsGuarded(
        ISimpleAssignmentOperation assignment,
        IFieldSymbol field,
        Dictionary<ILocalSymbol, IFieldSymbol> copies,
        out ILocalSymbol? copy)
    {
        copy = null;

        if (assignment.Parent is not (IExpressionStatementOperation or IReturnOperation) || assignment.Parent is not { } statement)
        {
            return false;
        }

        IOperation inner = statement;
        IOperation? parent = statement.Parent;

        if (parent is IBlockOperation block)
        {
            inner = block;
            parent = block.Parent;
        }

        return parent is IConditionalOperation conditional
            && conditional.WhenTrue == inner
            && IsSentinelTest(conditional.Condition, field, copies, out copy);
    }

    private static bool IsSentinelTest(IOperation condition, IFieldSymbol field, Dictionary<ILocalSymbol, IFieldSymbol> copies, out ILocalSymbol? copy)
    {
        copy = null;

        switch (Strip(condition))
        {
            case IBinaryOperation { OperatorKind: BinaryOperatorKind.Equals } equals:
                return (IsSubject(equals.LeftOperand, field, copies, out copy) && IsSentinel(equals.RightOperand))
                    || (IsSubject(equals.RightOperand, field, copies, out copy) && IsSentinel(equals.LeftOperand));

            case IIsPatternOperation { Pattern: IConstantPatternOperation constant } pattern:
                return IsSubject(pattern.Value, field, copies, out copy) && IsSentinel(constant.Value);

            default:
                return false;
        }
    }

    private static bool IsSubject(IOperation operation, IFieldSymbol field, Dictionary<ILocalSymbol, IFieldSymbol> copies, out ILocalSymbol? copy)
    {
        copy = null;

        switch (Strip(operation))
        {
            case IFieldReferenceOperation reference:
                return SymbolEqualityComparer.Default.Equals(reference.Field.OriginalDefinition, field) && IsThis(reference.Instance);

            case ILocalReferenceOperation { Local: { } local }
                when copies.TryGetValue(local, out IFieldSymbol? of) && SymbolEqualityComparer.Default.Equals(of, field):
                copy = local;
                return true;

            default:
                return false;
        }
    }

    private static bool IsSentinel(IOperation operation)
    {
        IOperation value = Strip(operation);
        return value.ConstantValue.HasValue || value is IDefaultValueOperation;
    }

    private static bool IsStaticFactory(IOperation operation) => Strip(operation) switch
    {
        IDelegateCreationOperation { Target: IAnonymousFunctionOperation lambda } => lambda.Symbol.IsStatic,
        IDelegateCreationOperation { Target: IMethodReferenceOperation method } => method.Method.IsStatic,
        _ => false,
    };

    /// <summary>
    /// Whether <paramref name="target"/> is written where it stands: assigned, stepped, passed by
    /// <c>ref</c> or <c>out</c>, taken by <c>ref</c>, or returned by <c>ref</c>.
    /// </summary>
    private static bool IsWritten(IOperation target, ISymbol member)
    {
        IOperation current = target;

        // (hash, other) = ...: a deconstruction's target is a tuple of targets.
        while (current.Parent is ITupleOperation tuple)
        {
            current = tuple;
        }

        return current.Parent switch
        {
            ISimpleAssignmentOperation { IsRef: true } refAssignment => refAssignment.Value == current || refAssignment.Target == current,
            IAssignmentOperation assignment => assignment.Target == current, // a deconstruction too
            IIncrementOrDecrementOperation step => step.Target == current,
            IArgumentOperation { Parameter.RefKind: RefKind.Ref or RefKind.Out } => true,
            IVariableInitializerOperation { Parent: IVariableDeclaratorOperation { Symbol.RefKind: RefKind.Ref } } => true,
            IReturnOperation => member is IMethodSymbol { ReturnsByRef: true },
            IAddressOfOperation => true,
            _ => false,
        };
    }

    /// <summary>
    /// Why <paramref name="value"/> is not a computation the memo may hold, or null when it is.
    /// </summary>
    /// <remarks>
    /// It may read readonly fields, <c>[Memoized]</c> fields, constants, parameters, locals it
    /// declares itself and <paramref name="copy"/>; read a property that has no setter, or whose
    /// setter is on some other value; and call a static method, a member of an immutable type, a
    /// read on a held collection, <c>object</c>'s own members, or a method of this type that DD0019's
    /// body check finds changes nothing (<paramref name="changesNothing"/>). Anything else, a write
    /// or a call through a delegate included, is the reason.
    /// </remarks>
    internal static string? Impurity(IOperation value, ILocalSymbol? copy, INamedTypeSymbol type, Func<IMethodSymbol, bool> changesNothing)
    {
        foreach (IOperation operation in value.DescendantsAndSelf())
        {
            if (Reason(operation, own => IsOwnLocal(own, value, copy), type, changesNothing) is { } reason)
            {
                return reason;
            }
        }

        return null;
    }

    /// <summary>Why one operation is outside the expression rule, or null; <paramref name="own"/> says which locals are the computation's own.</summary>
    private static string? Reason(IOperation operation, Func<IOperation, bool> own, INamedTypeSymbol type, Func<IMethodSymbol, bool> changesNothing)
    {
        switch (operation)
        {
            case IAssignmentOperation { Target: { } target } when !own(target):
                return $"a write to '{target.Syntax}'";

            case IIncrementOrDecrementOperation { Target: { } stepped } when !own(stepped):
                return $"a write to '{stepped.Syntax}'";

            case IDeconstructionAssignmentOperation deconstruction:
                return $"a write to '{deconstruction.Target.Syntax}'";

            case IArgumentOperation { Parameter.RefKind: RefKind.Ref or RefKind.Out, Value: { } passed } when !own(passed):
                return $"a write to '{passed.Syntax}'";

            case IFieldReferenceOperation { Field: { } field }
                when !field.IsConst && !field.IsReadOnly && !Markers.Has(field, Markers.Memoized):
                return $"'{field.Name}', which is neither readonly nor [Memoized]";

            case IPropertyReferenceOperation { Property: { SetMethod: { IsInitOnly: false } } property } read
                when property.IsStatic || IsThis(read.Instance):
                return $"'{property.Name}', which has a setter";

            case IInvocationOperation invocation when Call(invocation.TargetMethod, invocation.Instance, type, changesNothing) is { } call:
                return call;

            case IMethodReferenceOperation method when Call(method.Method, method.Instance, type, changesNothing) is { } reference:
                return reference;

            case IInstanceReferenceOperation { ReferenceKind: InstanceReferenceKind.ContainingTypeInstance } self when !IsReceiver(self):
                return "'this' itself, which the computation could change";

            case ILocalReferenceOperation local when !own(local):
                return $"the local '{local.Local.Name}', which is computed outside the memo";
        }

        return null;
    }

    private static string? Call(IMethodSymbol called, IOperation? instance, INamedTypeSymbol type, Func<IMethodSymbol, bool> changesNothing)
    {
        if (called.MethodKind == MethodKind.DelegateInvoke)
        {
            return "a call through a delegate, which the rule cannot show changes nothing";
        }

        if (called.IsStatic || called.ContainingType?.SpecialType == SpecialType.System_Object)
        {
            return null;
        }

        IOperation? receiver = instance is null ? null : Strip(instance);

        if (IsThis(receiver) && SymbolEqualityComparer.Default.Equals(called.ContainingType, type))
        {
            return changesNothing(called.OriginalDefinition)
                ? null
                : $"'{called.Name}', which the rule cannot show changes nothing";
        }

        if (receiver?.Type is { } held && (IsImmutable(held) || HeldCollectionReads.IsRead(called, held)))
        {
            return null;
        }

        return $"'{called.Name}' on '{receiver?.Type?.ToDisplayString(Display.Format) ?? "?"}', which the rule cannot show changes nothing";
    }

    private static bool IsOwnLocal(IOperation operation, IOperation value, ILocalSymbol? copy)
    {
        if (Strip(operation) is not ILocalReferenceOperation { Local: { } local })
        {
            return false;
        }

        if (SymbolEqualityComparer.Default.Equals(local, copy))
        {
            return true;
        }

        foreach (SyntaxReference declaration in local.DeclaringSyntaxReferences)
        {
            if (declaration.SyntaxTree == value.Syntax.SyntaxTree && value.Syntax.Span.Contains(declaration.Span))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary><c>this</c> as the receiver of a member access, which is how a member reads its own state.</summary>
    private static bool IsReceiver(IInstanceReferenceOperation self) => self.Parent switch
    {
        IMemberReferenceOperation member => member.Instance == self,
        IInvocationOperation invocation => invocation.Instance == self,
        _ => false,
    };

    private static bool IsThis(IOperation? operation) =>
        operation is IInstanceReferenceOperation { ReferenceKind: InstanceReferenceKind.ContainingTypeInstance };

    private static IOperation Strip(IOperation operation)
    {
        while (operation is IConversionOperation { IsImplicit: true } conversion)
        {
            operation = conversion.Operand;
        }

        return operation;
    }

    /// <summary>How a member is named in a finding: an accessor by its property, a constructor by its type.</summary>
    internal static string MemberName(ISymbol member) => member switch
    {
        IMethodSymbol { MethodKind: MethodKind.Constructor } constructor => constructor.ContainingType.Name,
        IMethodSymbol { AssociatedSymbol: { } associated } => associated.Name,
        _ => member.Name,
    };
}
