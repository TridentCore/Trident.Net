using System.Diagnostics.CodeAnalysis;
using TridentCore.Abstractions;

namespace TridentCore.Core.Utilities;

public static class AssetHelper
{
    private static bool TryResolveNonSymlinkDirectory(
        string root,
        ReadOnlySpan<string> pathSegments,
        [MaybeNullWhen(false)] out DirectoryInfo directory
    )
    {
        directory = null;

        var current = root;
        if (!(Directory.Exists(current) && Directory.ResolveLinkTarget(current, false) is null))
        {
            return false;
        }

        foreach (var segment in pathSegments)
        {
            current = Path.Combine(current, segment);
            if (!(Directory.Exists(current) && Directory.ResolveLinkTarget(current, false) is null))
            {
                return false;
            }
        }

        directory = new DirectoryInfo(current);
        return true;
    }

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
        ReadOnlySpan<string> pathSegments
    )
    {
        var storages = new[]
        {
            PathDef.Default.DirectoryOfBuild(key),
            PathDef.Default.DirectoryOfImport(key),
            PathDef.Default.DirectoryOfPersist(key),
        };

        var results = new List<FileInfo>();
        foreach (var storage in storages)
        {
            if (TryResolveNonSymlinkDirectory(storage, pathSegments, out var dir))
            {
                results.AddRange(
                    dir.GetFiles(pattern, SearchOption.TopDirectoryOnly)
                        .Where(x => x.LinkTarget is null)
                );
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
        ReadOnlySpan<string> pathSegments
    )
    {
        var storages = new[]
        {
            PathDef.Default.DirectoryOfBuild(key),
            PathDef.Default.DirectoryOfImport(key),
            PathDef.Default.DirectoryOfPersist(key),
        };

        var results = new List<DirectoryInfo>();
        foreach (var storage in storages)
        {
            if (TryResolveNonSymlinkDirectory(storage, pathSegments, out var dir))
            {
                results.AddRange(
                    dir.GetDirectories(pattern, SearchOption.TopDirectoryOnly)
                        .Where(x => x.LinkTarget is null)
                );
            }
        }

        return results;
    }
}
