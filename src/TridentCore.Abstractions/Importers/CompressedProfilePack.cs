using System.IO.Compression;
using TridentCore.Abstractions.Repositories.Resources;

namespace TridentCore.Abstractions.Importers;

public class CompressedProfilePack : IDisposable
{
    private readonly ZipArchive _archive;

    // input should be MemoryStream in practice
    public CompressedProfilePack(Stream input)
    {
        _archive = new(input, ZipArchiveMode.Read, false);
        FileNames = [.. _archive.Entries.Select(x => x.FullName)];
    }

    public IReadOnlyList<string> FileNames { get; }
    public Package? Reference { get; set; }

    #region IDisposable Members

    public void Dispose() => _archive.Dispose();

    #endregion

    public Stream Open(string fileName) => _archive.GetEntry(fileName)!.Open();
}
