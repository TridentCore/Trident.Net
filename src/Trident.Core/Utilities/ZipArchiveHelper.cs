using System.Diagnostics.CodeAnalysis;
using System.IO.Compression;

namespace Trident.Core.Utilities;

public static class ZipArchiveHelper
{
    public static readonly string[] InvalidNames = ["", ".", ".."];

    /// <summary>
    ///     检查压缩包是否只有一个共同目录
    /// </summary>
    /// <param name="archive">要检查的ZipArchive</param>
    /// <param name="rootDirName">如果存在单根目录，返回该目录名，否则为空串（表示根目录名）</param>
    /// <returns>如果所有文件都在一个目录内，返回 true</returns>
    public static bool HasSingleRootDirectory(ZipArchive archive, [MaybeNullWhen(false)] out string rootDirName)
    {
        rootDirName = string.Empty;

        if (archive.Entries.Count == 0)
        {
            return false;
        }

        // 获取所有条目的路径
        var entries = archive.Entries.Where(x => !InvalidNames.Contains(x.Name)).Select(e => e.FullName).ToList();

        var prefix = entries.FirstOrDefault();
        if (prefix == null)
        {
            return false;
        }

        rootDirName = prefix;
        foreach (var path in entries)
        {
            if (!string.IsNullOrEmpty(path) && !path.StartsWith(rootDirName))
            {
                if (rootDirName.Contains('/'))
                {
                    rootDirName = rootDirName[..rootDirName.LastIndexOf('/')];
                }
                else
                {
                    rootDirName = string.Empty;
                    break;
                }
            }
        }

        return true;
    }
}
