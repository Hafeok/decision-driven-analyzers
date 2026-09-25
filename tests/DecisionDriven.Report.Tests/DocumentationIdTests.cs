using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection.Metadata;
using DecisionDriven.Report.Metadata;
using DecisionDriven.Report.Tests.Fixtures;
using Microsoft.CodeAnalysis;
using Xunit;

namespace DecisionDriven.Report.Tests;

/// <summary>
/// Documentation-comment ids built from metadata match the ones Roslyn builds from source.
/// </summary>
/// <remarks>
/// The projection names every citing symbol this way, and the value is only useful if it is the same
/// string the compiler, the IDE and the XML docs use. So the test does not assert a hand-written
/// expectation: it compiles one fixture, asks Roslyn for every member's id, and requires the
/// metadata reader to produce exactly the same set.
/// </remarks>
public sealed class DocumentationIdTests
{
    private const string Source = @"
namespace Fix.Ids
{
    public sealed class Plain
    {
        public Plain(int value) { }
        public int Count { get; }
        public int this[int index, string key] => 0;
        public event System.EventHandler? Changed;
        public int Field;
        public void Many(int a, string[] b, System.Collections.Generic.List<int> c, ref long d, out int e) { e = 0; }
        public T Generic<T, U>(T value, System.Collections.Generic.IDictionary<T, U[]> map) => value;
        public static implicit operator int(Plain p) => 0;
        public static explicit operator Plain(long v) => new Plain(0);
        public static Plain operator +(Plain a, Plain b) => a;
    }

    public sealed class Outer<T>
    {
        public sealed class Inner
        {
            public T Take(T value, System.Collections.Generic.List<T> all) => value;
        }
    }

    public sealed class Explicit : System.IDisposable
    {
        void System.IDisposable.Dispose() { }
    }
}";

    [Fact]
    public void Every_member_id_matches_the_one_Roslyn_gives_it()
    {
        using Scratch scratch = new Scratch();
        Dictionary<string, string> files = new Dictionary<string, string> { [scratch.PathOf("Ids.cs")] = Source };

        string dll = Build.Assembly(scratch.PathOf("bin"), "Fix.Ids", files);

        HashSet<string> expected = new HashSet<string>(StringComparer.Ordinal);
        Compilation compilation = Build.Compilation("Fix.Ids", files);
        foreach (INamedTypeSymbol type in Types(compilation.GetSymbolsWithName(_ => true, SymbolFilter.Type, TestContext.Current.CancellationToken).OfType<INamedTypeSymbol>()))
        {
            expected.Add(type.GetDocumentationCommentId()!);

            foreach (ISymbol member in type.GetMembers())
            {
                // Accessors and the event's implicit field are not what a citation is ever written on,
                // and the metadata walk below does not visit them either.
                // A default constructor is implicitly declared in source and entirely real in metadata,
                // so it stays; nothing else implicit is.
                if ((member.IsImplicitlyDeclared && member is not IMethodSymbol { MethodKind: MethodKind.Constructor })
                    || member is IMethodSymbol { MethodKind: MethodKind.PropertyGet or MethodKind.PropertySet or MethodKind.EventAdd or MethodKind.EventRemove })
                {
                    continue;
                }

                expected.Add(member.GetDocumentationCommentId()!);
            }
        }

        HashSet<string> actual = new HashSet<string>(StringComparer.Ordinal);
        using LoadedAssembly assembly = LoadedAssembly.Open(dll)!;
        MetadataReader reader = assembly.Reader;

        foreach (TypeDefinitionHandle typeHandle in reader.TypeDefinitions)
        {
            if (!DecisionDriven.Report.Metadata.Types.IsWritten(reader, typeHandle))
            {
                continue;
            }

            TypeDefinition type = reader.GetTypeDefinition(typeHandle);
            actual.Add(DocumentationIds.Type(reader, typeHandle));

            HashSet<MethodDefinitionHandle> accessors = new HashSet<MethodDefinitionHandle>();
            foreach (PropertyDefinitionHandle property in type.GetProperties())
            {
                actual.Add(DocumentationIds.Property(reader, typeHandle, property));
                PropertyAccessors pair = reader.GetPropertyDefinition(property).GetAccessors();
                accessors.Add(pair.Getter);
                accessors.Add(pair.Setter);
            }

            foreach (EventDefinitionHandle @event in type.GetEvents())
            {
                actual.Add(DocumentationIds.Event(reader, typeHandle, @event));
                EventAccessors pair = reader.GetEventDefinition(@event).GetAccessors();
                accessors.Add(pair.Adder);
                accessors.Add(pair.Remover);
            }

            foreach (MethodDefinitionHandle method in type.GetMethods())
            {
                if (!accessors.Contains(method)
                    && !DecisionDriven.Report.Metadata.Types.IsCompilerGenerated(reader, reader.GetMethodDefinition(method).GetCustomAttributes()))
                {
                    actual.Add(DocumentationIds.Method(reader, method));
                }
            }

            foreach (FieldDefinitionHandle field in type.GetFields())
            {
                if (!DecisionDriven.Report.Metadata.Types.IsCompilerGenerated(reader, reader.GetFieldDefinition(field).GetCustomAttributes())
                    && !reader.GetString(reader.GetFieldDefinition(field).Name).Contains('<', StringComparison.Ordinal))
                {
                    actual.Add(DocumentationIds.Field(reader, typeHandle, field));
                }
            }
        }

        // The event's backing field is a real field in metadata that Roslyn does not surface as a
        // member; it is the one thing on the metadata side with no source counterpart.
        actual.Remove("F:Fix.Ids.Plain.Changed");

        // Both differences, so a failure says which side is missing what rather than where two sorted
        // lists first diverge.
        Assert.Empty(expected.Except(actual).OrderBy(id => id, StringComparer.Ordinal));
        Assert.Empty(actual.Except(expected).OrderBy(id => id, StringComparer.Ordinal));
    }

    private static IEnumerable<INamedTypeSymbol> Types(IEnumerable<INamedTypeSymbol> roots) =>
        roots.Where(type => type.ContainingNamespace.ToDisplayString().StartsWith("Fix.Ids", StringComparison.Ordinal));
}
