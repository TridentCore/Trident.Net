using TridentCore.Abstractions.Repositories.Resources;

namespace TridentCore.Core.Utilities;

// NOTE: 包在 build 内相对目标路径的唯一真源。FlattenPackages（冲突分组）、GenerateManifest
//  （物化）与 PackagePlanner（独立规划/导出）共用，三处不可能漂移。
public static class PackagePathHelper
{
    public static string RelativeTarget(
        bool normalizing,
        string? destination,
        string projectName,
        string fileName,
        ResourceKind kind)
    {
        var actual = normalizing
                         ? string.Concat(FileHelper.Sanitize(projectName), Path.GetExtension(fileName))
                         : fileName;
        if (destination is null)
        {
            return Path.Combine(FileHelper.GetAssetFolderName(kind), actual);
        }

        // NOTE: destination 来自整合包规则，必须相对且不含父目录引用，否则可在 build 外放置文件。
        if (Path.IsPathRooted(destination)
            || destination.Split('/', '\\').Any(x => x is ".." or "."))
        {
            throw new InvalidOperationException($"Unsafe package destination '{destination}'.");
        }

        return Path.Combine(destination, actual);
    }
}
