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
    /// <summary>The .editorconfig option that replaces the list.</summary>
    internal const string Option = "dd_banned_primitive_types";

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

    /// <summary>The configured list, or the one ADR-A09 names.</summary>
    /// <remarks>
    /// The option replaces the list rather than adding to it. A consumer that wants the default plus
    /// one writes the default plus one, which is longer and is also the only version of the setting
    /// whose meaning can be read off the file.
    /// </remarks>
    internal static BannedPrimitives Read(AnalyzerConfigOptions options)
    {
        HashSet<string> names = new HashSet<string>(StringComparer.Ordinal);

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

        if (names.Count == 0)
        {
            foreach (string name in Default)
            {
                names.Add(name);
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
