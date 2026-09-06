using System.IO.Compression;
using TridentCore.Abstractions.Repositories.Resources;

namespace TridentCore.Abstractions.Importers;

// 归档到「包内容」的唯一视图。外层包装目录（如手工重新压缩产生的 "整合包名/"、codeload 的
// "repo-sha/"）在此剥离，导入器只看见包自身的结构。
public class CompressedProfilePack : IDisposable
{
    private readonly ZipArchive _archive;

    // 剥离掉的外层包装目录，恒带尾斜杠；无包装时为空串。归档条目恒用 '/'，与平台分隔符无关。
    private readonly string _prefix;

    public CompressedProfilePack(Stream input)
    {
        _archive = new(input, ZipArchiveMode.Read, false);
        var physical = _archive.Entries.Select(x => x.FullName).ToList();
        _prefix = DetectRootPrefix(physical) ?? string.Empty;

        // WARNING: 包装目录自身的条目剥离后是空串，不是任何文件的名字，必须排除；否则
        //  Contains("") 之类的判定会命中它，且解压时会试图写一个无名文件。
        FileNames =
        [
            .. physical
              .Select(x => x[_prefix.Length..])
              .Where(x => !string.IsNullOrEmpty(x))
        ];
    }

    // 包内路径（已剥离外层包装目录）。
    public IReadOnlyList<string> FileNames { get; }

    public Package? Reference { get; set; }

    #region IDisposable Members

    public void Dispose() => _archive.Dispose();

    #endregion

    // 单个顶层目录包住全部条目时返回它，否则 null（扁平归档）。
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
        _archive.GetEntry(_prefix + fileName)?.Open()
     ?? throw new FileNotFoundException($"Entry '{fileName}' not found in the profile pack.");

    public long? LengthOf(string fileName) => _archive.GetEntry(_prefix + fileName)?.Length;
}
