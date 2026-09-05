using System.Text.Json;
using TridentCore.Abstractions;
using TridentCore.Abstractions.FileModels;
using TridentCore.Abstractions.LaunchPlans;
using TridentCore.Abstractions.Utilities;

namespace TridentCore.Core.Utilities;

// launch/ 目录的一次读取结果：按叠加顺序排好的层，加一个覆盖整棵树的内容指纹。
public sealed record LaunchPlanSnapshot(IReadOnlyList<LaunchPlanSnapshot.Entry> Entries, string Hash)
{
    public IReadOnlyList<LaunchLayer> ToLayers() =>
        [.. Entries.Select(x => new LaunchLayer(x.Origin, x.RelativePath, x.Operations))];

    public static LaunchPlanSnapshot? LoadOrNull(string key)
    {
        var launchDirectory = PathDef.Default.DirectoryOfLaunch(key);
        if (!Directory.Exists(launchDirectory))
        {
            return null;
        }

        var managed = EnumeratePlanFiles(PathDef.Default.DirectoryOfLaunchSource(key));
        var user = EnumeratePlanFiles(launchDirectory);
        if (managed.Count == 0 && user.Count == 0)
        {
            return null;
        }

        var entries = managed
                     .Select(x => Load(x, launchDirectory, LaunchLayer.Origin.Managed))
                     .Concat(user.Select(x => Load(x, launchDirectory, LaunchLayer.Origin.User)))
                     .ToArray();
        return new(entries, ComputeTreeHash(launchDirectory));
    }

    // 指纹覆盖 launch/ 下的每一个文件，包括点号前缀禁用的计划与未被任何计划引用的文件。
    // WARNING: 故意做粗——只按"树里有任何字节变了"失效，代价是偶尔多一次重建。不要改成只追踪
    //  被引用的文件：那要求指纹与引用解析保持同步，漏掉一处即产生"改了文件却复用旧产物"，
    //  且这种漏判不会以任何可见方式报错。
    private static string ComputeTreeHash(string launchDirectory) =>
        HashHelper.ComputeObjectHash(Directory
                                    .EnumerateFiles(launchDirectory, "*", SearchOption.AllDirectories)
                                    .Select(x => new TreeEntry(Relative(launchDirectory, x),
                                                               FileHelper.ComputeHash(x, HashAlgorithm.Sha256)))
                                    .OrderBy(x => x.RelativePath, StringComparer.Ordinal)
                                    .ToArray());

    // 只扫直接文件——source/ 与 launch/ 根各自是一层的平坦集合，子目录留给被引用的本地文件。
    private static IReadOnlyList<string> EnumeratePlanFiles(string directory) =>
        Directory.Exists(directory)
            ? [.. Directory
                 .EnumerateFiles(directory, "*", SearchOption.TopDirectoryOnly)
                 .Where(LaunchPlanFileHelper.IsEnabledPlanFile)
                 .OrderBy(Path.GetFileName, StringComparer.Ordinal)]
            : [];

    private static Entry Load(string path, string launchDirectory, LaunchLayer.Origin origin)
    {
        var relativePath = Relative(launchDirectory, path);
        LaunchPlanDocument document;
        try
        {
            document = JsonSerializer.Deserialize<LaunchPlanDocument>(File.ReadAllText(path), JsonSerializerOptions.Web)
                    ?? throw new FormatException($"Launch plan '{relativePath}' is empty");
        }
        catch (JsonException e)
        {
            throw new FormatException($"Launch plan '{relativePath}' is not valid JSON", e);
        }

        var resolved = document.Operations.Select(x => Resolve(x, Path.GetDirectoryName(Path.GetFullPath(path))!,
                                                              launchDirectory,
                                                              origin))
                               .ToArray();
        try
        {
            LaunchPlanValidator.Validate(resolved);
        }
        catch (FormatException e)
        {
            throw new FormatException($"Launch plan '{relativePath}' contains an invalid operation", e);
        }

        return new(Path.GetFullPath(path), relativePath, origin, resolved, document.Operations);
    }

    private static LaunchPlanDocument.Operation Resolve(
        LaunchPlanDocument.Operation operation,
        string planDirectory,
        string launchDirectory,
        LaunchLayer.Origin origin) =>
        operation switch
        {
            LaunchPlanDocument.AddLibraryOperation add => add with
            {
                Value = add.Value with
                {
                    Url = ResolveUri(add.Value.Url, planDirectory, launchDirectory, origin)
                }
            },
            LaunchPlanDocument.SetAssetIndexOperation set => set with
            {
                Value = set.Value with
                {
                    Url = ResolveUri(set.Value.Url, planDirectory, launchDirectory, origin)
                }
            },
            _ => operation
        };

    // 相对引用以所属计划文件所在目录为基准，解析为绝对 file:// URI；内容指纹由整棵树承担，
    // 此处不再逐个累计被引用文件的 hash。
    private static Uri ResolveUri(
        Uri uri,
        string planDirectory,
        string launchDirectory,
        LaunchLayer.Origin origin)
    {
        if (uri.IsAbsoluteUri && !uri.IsFile)
        {
            return uri;
        }

        var fullPath = Path.GetFullPath(uri.IsAbsoluteUri ? uri.LocalPath : Path.Combine(planDirectory, uri.OriginalString));

        // NOTE: 托管层来自整合包这类不可信输入，其引用必须留在 launch/ 内；用户层是本机自维护的，
        //  允许指向任意本地路径。
        if (origin == LaunchLayer.Origin.Managed && !FileHelper.IsInDirectory(fullPath, launchDirectory))
        {
            throw new FormatException($"Launch plan file reference '{uri}' escapes the launch directory");
        }

        if (!File.Exists(fullPath))
        {
            throw new FileNotFoundException($"Launch plan file reference '{uri}' does not exist", fullPath);
        }

        return new(fullPath, UriKind.Absolute);
    }

    private static string Relative(string launchDirectory, string path) =>
        Path.GetRelativePath(launchDirectory, path).Replace(Path.DirectorySeparatorChar, '/');

    #region Nested type: Entry

    // Operations 是解析后的形态（本地引用已成绝对 file:// URI），供部署消费；
    // RawOperations 保持文件原样，供导出还原成来源格式。
    public sealed record Entry(
        string Path,
        string RelativePath,
        LaunchLayer.Origin Origin,
        IReadOnlyList<LaunchPlanDocument.Operation> Operations,
        IReadOnlyList<LaunchPlanDocument.Operation> RawOperations);

    #endregion

    #region Nested type: TreeEntry

    // 具名 record 而非 ValueTuple——字段名会进入序列化结果，指纹因此对“路径与内容分别是什么”
    // 敏感，而不只是对位置敏感。
    private sealed record TreeEntry(string RelativePath, string Hash);

    #endregion
}
