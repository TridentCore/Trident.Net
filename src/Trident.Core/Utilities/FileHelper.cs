using MimeDetective;
using MimeDetective.Definitions;
using Trident.Abstractions.Repositories.Resources;
using FileStream = System.IO.FileStream;

namespace Trident.Core.Utilities;

public static class FileHelper
{
    private static readonly string[] SUPPORTED_BITMAP_MIMES =
    [
        "image/jpeg", "image/png", "image/bmp", "image/gif", "image/tiff"
    ];

    private static readonly IContentInspector Inspector =
        new ContentInspectorBuilder { Definitions = DefaultDefinitions.All() }.Build();

    public static string? PickExists(string home, Span<string> candidates)
    {
        foreach (var candidate in candidates)
        {
            var path = Path.Combine(home, candidate);
            if (File.Exists(path))
            {
                return path;
            }
        }

        return null;
    }

    public static bool IsBitmapFile(string path)
    {
        if (File.Exists(path))
        {
            var results = Inspector.Inspect(path).ByMimeType();
            if (results.Any(x => SUPPORTED_BITMAP_MIMES.Contains(x.MimeType)))
            {
                return true;
            }
        }

        return false;
    }

    public static string GuessBitmapExtension(Stream stream, string fallback = "png") =>
        Inspector.Inspect(stream).ByFileExtension().OrderBy(x => -x.Points).Select(x => x.Extension).FirstOrDefault()
     ?? fallback;

    public static bool IsInDirectory(string file, string directory) =>
        Path
           .GetFullPath(file)
           .StartsWith(Path.GetFullPath(directory),
                       OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);

    public static async Task TryWriteToFileAsync(string path, Stream stream)
    {
        var parent = Path.GetDirectoryName(path);
        if (parent != null && !Directory.Exists(parent))
        {
            Directory.CreateDirectory(parent);
        }

        var writer = new FileStream(path, FileMode.Create, FileAccess.Write);
        await stream.CopyToAsync(writer).ConfigureAwait(false);
        await writer.FlushAsync().ConfigureAwait(false);
        writer.Close();
    }

    public static string GetAssetFolderName(ResourceKind kind) =>
        kind switch
        {
            ResourceKind.Mod => "mods",
            ResourceKind.World => "saves",
            ResourceKind.ShaderPack => "shaderpacks",
            ResourceKind.ResourcePack => "resourcepacks",
            ResourceKind.DataPack => "resourcepacks",
            _ => throw new NotImplementedException()
        };

    public static (ulong, ulong) CalculateDirectorySize(string path)
    {
        if (!Directory.Exists(path))
        {
            return (0ul, 0ul);
        }

        var directory = new DirectoryInfo(path);
        var (size, count) = directory
                           .GetFiles()
                           .Aggregate((0ul, 0ul),
                                      (current, file) => (current.Item1 + (ulong)file.Length, current.Item2 + 1));
        return directory
              .GetDirectories()
              .Aggregate((size, count),
                         (current, dir) =>
                         {
                             var (subSize, subCount) = CalculateDirectorySize(dir.FullName);
                             return (current.size + subSize, current.count + subCount);
                         });
    }
}
