using System;
using System.IO;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Text.RegularExpressions;

namespace DecisionDriven.Report.Metadata;

/// <summary>
/// Which source file a symbol was written in, from the portable PDB.
/// </summary>
/// <remarks>
/// The projection finds a citation's introducing commit by searching that file's history, so it
/// needs the file. A method with a body has sequence points that name it. A type with no method
/// bodies - an interface, most contracts - has none, and for those the compiler writes a
/// <c>TypeDefinitionDocuments</c> record instead, which is read here as well.
/// </remarks>
internal static class Sources
{
    private static readonly Guid TypeDefinitionDocuments = new Guid("932E74BC-DBA9-4478-8D46-0F32A7BAB3D3");

    /// <summary>
    /// A deterministic build maps its source root to <c>/_/</c> (and further roots to
    /// <c>/_1/</c>...), which is what a CI build of this repository produces.
    /// </summary>
    private static readonly Regex MappedRoot = new Regex(@"^/_\d*/", RegexOptions.CultureInvariant);

    internal static string? OfType(LoadedAssembly assembly, TypeDefinitionHandle handle)
    {
        if (assembly.Pdb is not { } pdb)
        {
            return null;
        }

        TypeDefinition type = assembly.Reader.GetTypeDefinition(handle);

        foreach (MethodDefinitionHandle method in type.GetMethods())
        {
            if (OfMethod(assembly, method) is { } file)
            {
                return file;
            }
        }

        foreach (CustomDebugInformationHandle cdiHandle in pdb.GetCustomDebugInformation(handle))
        {
            CustomDebugInformation cdi = pdb.GetCustomDebugInformation(cdiHandle);
            if (pdb.GetGuid(cdi.Kind) != TypeDefinitionDocuments)
            {
                continue;
            }

            BlobReader blob = pdb.GetBlobReader(cdi.Value);
            while (blob.RemainingBytes > 0)
            {
                DocumentHandle document = MetadataTokens.DocumentHandle(blob.ReadCompressedInteger());
                return pdb.GetString(pdb.GetDocument(document).Name);
            }
        }

        // A nested type is written in its declaring type's file.
        TypeDefinitionHandle declaring = type.GetDeclaringType();
        return declaring.IsNil ? null : OfType(assembly, declaring);
    }

    internal static string? OfMethod(LoadedAssembly assembly, MethodDefinitionHandle handle)
    {
        if (assembly.Pdb is not { } pdb)
        {
            return null;
        }

        MethodDebugInformation info = pdb.GetMethodDebugInformation(handle.ToDebugInformationHandle());

        if (!info.Document.IsNil)
        {
            return pdb.GetString(pdb.GetDocument(info.Document).Name);
        }

        foreach (SequencePoint point in info.GetSequencePoints())
        {
            if (!point.Document.IsNil)
            {
                return pdb.GetString(pdb.GetDocument(point.Document).Name);
            }
        }

        return null;
    }

    /// <summary>
    /// A PDB document path as a path in the repository, or null when it is not in it.
    /// </summary>
    internal static string? InRepository(string documentPath, string repositoryRoot)
    {
        string path = documentPath.Replace('\\', '/');

        Match mapped = MappedRoot.Match(path);
        if (mapped.Success)
        {
            return path.Substring(mapped.Length);
        }

        string root = Path.GetFullPath(repositoryRoot).Replace('\\', '/').TrimEnd('/') + "/";

        // Windows paths compare without case, because the file system does.
        StringComparison comparison = Path.DirectorySeparatorChar == '\\'
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;

        return path.StartsWith(root, comparison) ? path.Substring(root.Length) : null;
    }
}
