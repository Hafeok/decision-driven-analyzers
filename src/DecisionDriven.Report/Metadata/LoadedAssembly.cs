using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;

namespace DecisionDriven.Report.Metadata;

/// <summary>
/// One built assembly, read as metadata and never loaded.
/// </summary>
/// <remarks>
/// <c>WholeGraphReport.WholeGraphMetricsAreCiTool</c> puts these metrics over built assemblies, and
/// reading them as metadata is what lets the tool run over assemblies whose dependencies are not on
/// its own load path - which is every consumer's. The portable PDB comes along when there is one,
/// embedded or beside the assembly, because the citation projection needs to know which file a
/// symbol was written in.
/// </remarks>
internal sealed class LoadedAssembly : IDisposable
{
    private readonly PEReader pe;
    private readonly MetadataReaderProvider? pdbProvider;

    private LoadedAssembly(string path, PEReader pe, MetadataReaderProvider? pdbProvider)
    {
        Path = path;
        this.pe = pe;
        this.pdbProvider = pdbProvider;
        Reader = pe.GetMetadataReader();
        Pdb = pdbProvider?.GetMetadataReader();
        Name = Reader.GetString(Reader.GetAssemblyDefinition().Name);
    }

    internal string Path { get; }

    internal string Name { get; }

    internal MetadataReader Reader { get; }

    /// <summary>The portable PDB, or null when there is none to read.</summary>
    internal MetadataReader? Pdb { get; }

    /// <summary>Opens an assembly, or returns null when the file is not one.</summary>
    internal static LoadedAssembly? Open(string path)
    {
        FileStream stream = File.OpenRead(path);
        PEReader pe = new PEReader(stream);

        try
        {
            if (!pe.HasMetadata || !pe.GetMetadataReader().IsAssembly)
            {
                pe.Dispose();
                return null;
            }
        }
        catch (BadImageFormatException)
        {
            pe.Dispose();
            return null;
        }

        MetadataReaderProvider? pdb = null;

        try
        {
            // Embedded first, then beside the assembly. A Windows PDB beside it is not portable and
            // is not readable here; the projection then falls back to searching the repository.
            pe.TryOpenAssociatedPortablePdb(
                path,
                candidate => File.Exists(candidate) ? File.OpenRead(candidate) : null,
                out pdb,
                out _);
        }
        catch (BadImageFormatException)
        {
            pdb = null;
        }

        return new LoadedAssembly(path, pe, pdb);
    }

    /// <summary>The IL of a method, or null when it has none.</summary>
    internal MethodBodyBlock? Body(MethodDefinition method) =>
        method.RelativeVirtualAddress == 0 ? null : pe.GetMethodBody(method.RelativeVirtualAddress);

    /// <summary>The simple names of the assemblies this one references.</summary>
    internal IEnumerable<string> References()
    {
        foreach (AssemblyReferenceHandle handle in Reader.AssemblyReferences)
        {
            yield return Reader.GetString(Reader.GetAssemblyReference(handle).Name);
        }
    }

    public void Dispose()
    {
        pdbProvider?.Dispose();
        pe.Dispose();
    }
}
