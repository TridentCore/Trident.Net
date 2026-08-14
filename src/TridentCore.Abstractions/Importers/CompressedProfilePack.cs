using System.IO.Compression;
using TridentCore.Abstractions.Repositories.Resources;

namespace TridentCore.Abstractions.Importers;

public class CompressedProfilePack : IDisposable
{
    private readonly ZipArchive _archive;

    // NOTE: 实践中 input 应为 MemoryStream。
    public CompressedProfilePack(Stream input)
    {
        _archive = new(input, ZipArchiveMode.Read, false);
        FileNames = [.. _archive.Entries.Select(x => x.FullName)];
        RootPrefix = DetectRootPrefix(FileNames);
    }

    // NOTE: null = 扁平归档；非 null = 单个顶层包装目录（如 codeload 的 "repo-sha/"），恒带尾斜杠。
    //  原样暴露，由各导入器显式决定是否剥离。
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

    public long? LengthOf(string fileName) => _archive.GetEntry(fileName)?.Length;
}
