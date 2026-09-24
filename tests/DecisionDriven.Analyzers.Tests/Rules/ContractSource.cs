using System;

namespace DecisionDriven.Analyzers.Tests.Rules;

/// <summary>
/// The marker attributes as a consumer's compilation has them.
/// </summary>
/// <remarks>
/// Written out rather than generated, because the contract rules are not about the generator: they
/// match the attributes by name and namespace the same way they will in a real consumer, and a
/// generator run in the middle of these tests would be a second thing that could fail.
/// </remarks>
internal static class ContractSource
{
    /// <summary>The attribute declarations, without the assembly-level attributes.</summary>
    internal const string Markers = @"
namespace DecisionDriven
{
    [global::System.AttributeUsage(global::System.AttributeTargets.Interface | global::System.AttributeTargets.Class | global::System.AttributeTargets.Delegate)]
    internal sealed class ContractAttribute : global::System.Attribute
    {
        public ContractAttribute(global::System.Type decision) { Decision = decision; }
        public global::System.Type Decision { get; }
        public string Role { get; set; } = string.Empty;
    }

    [global::System.AttributeUsage(global::System.AttributeTargets.Assembly, AllowMultiple = true)]
    internal sealed class DomainModelAttribute : global::System.Attribute
    {
        public DomainModelAttribute(string namespacePrefix, global::System.Type decision)
        {
            NamespacePrefix = namespacePrefix;
            Decision = decision;
        }

        public string NamespacePrefix { get; }
        public global::System.Type Decision { get; }
    }

    [global::System.AttributeUsage(global::System.AttributeTargets.All, AllowMultiple = true)]
    internal sealed class DesignDecisionAttribute : global::System.Attribute
    {
        public DesignDecisionAttribute(global::System.Type decision) { Decision = decision; }
        public global::System.Type Decision { get; }
        public ExceptionScope Scope { get; set; }
    }

    internal enum ExceptionScope { Boundary, HotPath, Pool, Interop, Compatibility, Migration }
}
";

    /// <summary>A decision type to cite. Whether it is a real one is DD0007's question.</summary>
    internal const string Decision = "typeof(object)";

    /// <summary>The <c>[Contract]</c> attribute, written the way a consumer writes it.</summary>
    internal const string Contract = "[global::DecisionDriven.Contract(" + Decision + ", Role = \"a role\")]";

    /// <summary>A file: assembly-level attributes first, then the source, then the markers.</summary>
    internal static string File(string source, string? assemblyAttributes = null) =>
        (assemblyAttributes ?? string.Empty) + Environment.NewLine + source + Environment.NewLine + Markers;
}
