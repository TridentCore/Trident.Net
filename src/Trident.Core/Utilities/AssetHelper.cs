using Trident.Abstractions;

namespace Trident.Core.Utilities;

public static class AssetHelper
{
    /// <summary>
    ///     在 build/import/persist 三个目录下的非 Symlink 目录中搜索非 Symlink 文件
    /// </summary>
    /// <param name="key">实例 Key </param>
    /// <param name="pattern">搜索模式</param>
    /// <param name="pathSegments">进一步搜索的目录段，例如 ["config", "jei"]</param>
    /// <returns></returns>
    public static IReadOnlyList<FileInfo> ScanNonSymlinks(string key, string pattern, ReadOnlySpan<string> pathSegments)
    {
        var storages = new[]
        {
            PathDef.Default.DirectoryOfBuild(key),
            PathDef.Default.DirectoryOfImport(key),
            PathDef.Default.DirectoryOfPersist(key)
        };

        var results = new List<FileInfo>();
        foreach (var storage in storages)
        {
            var dir = new DirectoryInfo(Path.Combine([storage, .. pathSegments]));
            if (dir.Exists && dir.ResolveLinkTarget(false) is null)
            {
                results.AddRange(dir
                                .GetFiles(pattern, SearchOption.TopDirectoryOnly)
                                .Where(file => file.ResolveLinkTarget(false) is null));
            }
        }

        return results;
    }
}
