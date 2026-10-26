using System;
using System.Collections.Generic;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Operations;

namespace DecisionDriven.Analyzers.Rules;

/// <summary>The two shapes a <c>[Memoized]</c> field may have.</summary>
internal enum MemoForm
{
    /// <summary>Not a memo DD0019 accepts; the field's shape is the finding.</summary>
    None,

    /// <summary>A private, non-readonly field of an immutable type, filled once.</summary>
    Value,

    /// <summary>A private readonly <c>ConcurrentDictionary&lt;K, V&gt;</c> filled through <c>GetOrAdd</c>.</summary>
    PerKey,
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
/// The attribute is a claim, not an exemption. The rule accepts exactly two forms, and anything it
/// cannot show is one of them stays a finding: the direction of error is the same as the rest of
/// DD0019's body check.
/// </para>
/// </remarks>
internal static class MemoizedFields
{
    private const string ConcurrentDictionary = "System.Collections.Concurrent.ConcurrentDictionary";

    private const string GetOrAdd = "GetOrAdd";

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

        if (IsPlainCollection(type))
        {
            return new Shape(
                MemoForm.None,
                $"{name} is [Memoized] and is a plain '{type.ToDisplayString(Display.Format)}', "
                    + "and a plain collection filled by concurrent readers is a race",
                "hold a readonly System.Collections.Concurrent.ConcurrentDictionary<K, V> filled only through "
                    + "GetOrAdd with a static factory, compute it in the constructor, or build it in a sealed *Builder");
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

        use.Finding = $"{at} hands out the [Memoized] '{field.Name}' itself, and whoever gets it can write to it";
        use.DesignChange = "return what GetOrAdd or a read returns, not the dictionary";
        return use;
    }

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
            switch (operation)
            {
                case IAssignmentOperation { Target: { } target } when !IsOwnLocal(target, value, copy):
                    return $"a write to '{target.Syntax}'";

                case IIncrementOrDecrementOperation { Target: { } stepped } when !IsOwnLocal(stepped, value, copy):
                    return $"a write to '{stepped.Syntax}'";

                case IDeconstructionAssignmentOperation deconstruction:
                    return $"a write to '{deconstruction.Target.Syntax}'";

                case IArgumentOperation { Parameter.RefKind: RefKind.Ref or RefKind.Out, Value: { } passed } when !IsOwnLocal(passed, value, copy):
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

                case ILocalReferenceOperation local when !IsOwnLocal(local, value, copy):
                    return $"the local '{local.Local.Name}', which is computed outside the memo";
            }
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
