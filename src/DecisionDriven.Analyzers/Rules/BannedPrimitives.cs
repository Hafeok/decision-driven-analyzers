using System;
using System.Collections.Generic;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;

namespace DecisionDriven.Analyzers.Rules;

/// <summary>
/// The types a model or contract surface does not say out loud.
/// </summary>
/// <remarks>
/// <para>
/// <c>PrimitiveFreeSurfaces.NoNakedPrimitivesOnModelAndContract</c>. The defect is
/// <c>Read(long position, ulong graphId)</c>: two arguments the compiler will happily let a caller
/// swap, standing in for two things that are not the same.
/// </para>
/// <para>
/// A list rather than a predicate, because "primitive" has no definition that works here: <c>int</c>
/// is a struct, <c>Guid</c> is a struct, and <c>Position</c> had better be one too.
/// </para>
/// </remarks>
internal sealed class BannedPrimitives
{
    /// <summary>The .editorconfig option that adds to the list.</summary>
    internal const string Option = "dd_banned_primitive_types_add";

    private static readonly string[] Default =
    {
        "string", "char",
        "sbyte", "byte", "short", "ushort", "int", "uint", "long", "ulong", "nint", "nuint",
        "float", "double", "decimal", "System.Half",
        "System.Guid",
        "System.DateTime", "System.DateTimeOffset", "System.TimeSpan", "System.DateOnly", "System.TimeOnly",
        "object",
    };

    private readonly HashSet<string> banned;

    private BannedPrimitives(HashSet<string> banned)
    {
        this.banned = banned;
    }

    /// <summary>ADR-A09's list, plus whatever the consumer added to it.</summary>
    /// <remarks>
    /// <para>
    /// <c>PrimitiveFreeSurfaces.BannedPrimitiveListIsAdditiveOnly</c>. The option adds and can never
    /// remove. A list a consumer could shorten would be a suppression path around DD0013 with no
    /// citation anywhere: <c>dd_banned_primitive_types = Guid</c> would silently unban
    /// <c>string</c> and <c>long</c> across a whole repository, which is exactly what
    /// <c>DecisionsAsTypes.NoPragmaOrSuppressMessage</c> exists to stop. Taking a type off the list
    /// is a superseding decision here, not a consumer setting.
    /// </para>
    /// <para>
    /// The name says so too. An option called <c>dd_banned_primitive_types</c> reads like the list,
    /// and somebody would set it to the one type they were thinking about.
    /// </para>
    /// </remarks>
    internal static BannedPrimitives Read(AnalyzerConfigOptions options)
    {
        HashSet<string> names = new HashSet<string>(StringComparer.Ordinal);

        foreach (string name in Default)
        {
            names.Add(name);
        }

        if (options.TryGetValue(Option, out string? configured) && configured is { Length: > 0 })
        {
            foreach (string name in configured.Split(',', ';'))
            {
                string trimmed = name.Trim();
                if (trimmed.Length > 0)
                {
                    names.Add(trimmed);
                }
            }
        }

        return new BannedPrimitives(names);
    }

    /// <summary>True for a type named in the list, by keyword or by full name.</summary>
    /// <remarks>
    /// Both spellings are matched because both get written. <c>long</c> and <c>System.Int64</c> are
    /// the same type, and a consumer who configured one and meant the other would get a list that
    /// silently matched nothing.
    /// </remarks>
    internal bool IsBanned(ITypeSymbol type) =>
        banned.Contains(type.ToDisplayString(Keyword))
        || banned.Contains(type.ToDisplayString(FullName));

    private static readonly SymbolDisplayFormat Keyword = new SymbolDisplayFormat(
        globalNamespaceStyle: SymbolDisplayGlobalNamespaceStyle.Omitted,
        typeQualificationStyle: SymbolDisplayTypeQualificationStyle.NameAndContainingTypesAndNamespaces,
        miscellaneousOptions: SymbolDisplayMiscellaneousOptions.UseSpecialTypes);

    private static readonly SymbolDisplayFormat FullName = new SymbolDisplayFormat(
        globalNamespaceStyle: SymbolDisplayGlobalNamespaceStyle.Omitted,
        typeQualificationStyle: SymbolDisplayTypeQualificationStyle.NameAndContainingTypesAndNamespaces);
}
