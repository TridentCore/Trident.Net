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
        RootPrefix = DetectRootPrefix(FileNames);
    }

    // null = flat archive; non-null = single top-level wrapper dir (e.g. codeload's "repo-sha/"),
    // always trailing '/'. Exposed verbatim so each importer decides explicitly whether to strip.
    public string? RootPrefix { get; }

    public IReadOnlyList<string> FileNames { get; }
    public Package? Reference { get; set; }

    #region IDisposable Members

    public void Dispose() => _archive.Dispose();

    #endregion

    private static string? DetectRootPrefix(IReadOnlyList<string> names)
    {
        string? prefix = null;
        foreach (var name in names)
        {
            var slash = name.IndexOf('/');
            if (slash < 0)
            {
                return null;
            }

            var top = name[..(slash + 1)];
            if (prefix is null)
            {
                prefix = top;
            }
            else if (prefix != top)
            {
                return null;
            }
        }

        return prefix;
    }

    public Stream Open(string fileName) =>
        _archive.GetEntry(fileName)?.Open()
     ?? throw new FileNotFoundException($"Entry '{fileName}' not found in the profile pack.");
}
