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
    /// <returns>文件信息</returns>
    public static IReadOnlyList<FileInfo> ScanNonSymlinkFiles(
        string key,
        string pattern,
        ReadOnlySpan<string> pathSegments)
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
            var outer = storage;
            var pass = true;
            foreach (var segment in pathSegments)
            {
                outer = Path.Combine(outer, segment);
                if (!(Directory.Exists(outer) && Directory.ResolveLinkTarget(outer, false) is null))
                {
                    pass = false;
                    break;
                }
            }

            if (pass)
            {
                var dir = new DirectoryInfo(outer);
                results.AddRange(dir.GetFiles(pattern, SearchOption.TopDirectoryOnly).Where(x => x.LinkTarget is null));
            }
        }

        return results;
    }

    /// <summary>
    ///     在 build/import/persist 三个目录下的非 Symlink 目录中搜索非 Symlink 目录
    /// </summary>
    /// <param name="key">实例 Key </param>
    /// <param name="pattern">搜索模式</param>
    /// <param name="pathSegments">进一步搜索的目录段，例如 ["saves"]</param>
    /// <returns>目录信息</returns>
    public static IReadOnlyList<DirectoryInfo> ScanNonSymlinkDirectories(
        string key,
        string pattern,
        ReadOnlySpan<string> pathSegments)
    {
        var storages = new[]
        {
            PathDef.Default.DirectoryOfBuild(key),
            PathDef.Default.DirectoryOfImport(key),
            PathDef.Default.DirectoryOfPersist(key)
        };

        var results = new List<DirectoryInfo>();
        foreach (var storage in storages)
        {
            var outer = storage;
            var pass = true;
            foreach (var segment in pathSegments)
            {
                outer = Path.Combine(outer, segment);
                if (!(Directory.Exists(outer) && Directory.ResolveLinkTarget(outer, false) is null))
                {
                    pass = false;
                    break;
                }
            }

            if (pass)
            {
                var dir = new DirectoryInfo(outer);
                results.AddRange(dir
                                .GetDirectories(pattern, SearchOption.TopDirectoryOnly)
                                .Where(x => x.LinkTarget is null));
            }
        }

        return results;
    }
}
