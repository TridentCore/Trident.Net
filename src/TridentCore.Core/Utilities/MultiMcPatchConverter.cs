using System.Diagnostics.CodeAnalysis;
using System.Text;
using System.Text.Json;
using TridentCore.Abstractions.FileModels;
using TridentCore.Abstractions.LaunchPlans;
using TridentCore.Abstractions.Utilities;
using TridentCore.Core.Models.MultiMcPack;

namespace TridentCore.Core.Utilities;

// MMC patch 到原生 operation 的转换。这里是外部约定的终点：MMC 的 url 多态（仓库根 /
// 私有绝对地址 / hint 本地文件 / 缺省）在此全部规范为原生 add-library 的唯一含义。
public static class MultiMcPatchConverter
{
    // MMC/Prism 在 library 既无 downloads 也无 url 时回落到的隐式仓库。
    private static readonly Uri DEFAULT_LIBRARY_REPOSITORY = new("https://libraries.minecraft.net/");

    public static ConversionResult Convert(string uid, MmcPatch patch)
    {
        var operations = new List<LaunchPlanDocument.Operation>();
        var diagnostics = new List<LaunchPlanDiagnostic>();

        if (!string.IsNullOrWhiteSpace(patch.MainClass))
        {
            operations.Add(new LaunchPlanDocument.SetMainClassOperation(patch.MainClass));
        }

        if (patch.MinecraftArguments is not null)
        {
            operations.Add(new LaunchPlanDocument.SetGameArgumentsOperation(SplitArguments(patch.MinecraftArguments)));
        }

        foreach (var argument in patch.JvmArguments ?? [])
        {
            operations.Add(new LaunchPlanDocument.AppendJavaArgumentOperation(argument));
        }

        foreach (var tweaker in patch.Tweakers ?? [])
        {
            operations.Add(new LaunchPlanDocument.AppendGameArgumentOperation("--tweakClass"));
            operations.Add(new LaunchPlanDocument.AppendGameArgumentOperation(tweaker));
        }

        if (patch.Traits is { Count: > 0 })
        {
            diagnostics.Add(new(LaunchPlanDiagnostic.Kind.Warning,
                                $"MMC patch '{uid}' traits are not represented by the native launch plan."));
        }

        // jarMods 是把条目重打包进客户端 jar（1.7.10 时代的 mod 注入），既不是启动参数也不是
        // 包引用，无法转为原生计划 operation，也接不到包仲裁层——如实报告为不支持。
        if (patch.JarMods is { Count: > 0 } jarMods)
        {
            diagnostics.Add(new(LaunchPlanDiagnostic.Kind.Error,
                                $"MMC patch '{uid}' declares {jarMods.Count} jar mod(s) that require client jar repackaging, which is not supported."));
        }

        if (patch.Agents is { Count: > 0 })
        {
            diagnostics.Add(new(LaunchPlanDiagnostic.Kind.Warning,
                                $"MMC patch '{uid}' Java agents are not represented by the native launch plan."));
        }

        if (patch.CompatibleJavaMajors is { Count: > 0 })
        {
            operations.Add(new LaunchPlanDocument.SetJavaMajorVersionOperation(patch.CompatibleJavaMajors[0]));
            if (patch.CompatibleJavaMajors.Count > 1)
            {
                diagnostics.Add(new(LaunchPlanDiagnostic.Kind.Warning,
                                    $"MMC patch '{uid}' has multiple compatible Java majors; the first was selected."));
            }
        }

        if (patch.AssetIndex is { } assetIndex)
        {
            operations.Add(new LaunchPlanDocument.SetAssetIndexOperation(new(assetIndex.Id,
                                                                              assetIndex.Url,
                                                                              FileHash.FromSha1(assetIndex.Sha1))));
        }

        foreach (var library in (patch.Libraries ?? []).Concat(patch.AdditionalLibraries ?? []))
        {
            if (!TryConvertLibrary(library, false, out var converted, out var diagnostic))
            {
                diagnostics.Add(new(LaunchPlanDiagnostic.Kind.Warning, diagnostic!));
                continue;
            }

            operations.Add(new LaunchPlanDocument.AddLibraryOperation(converted!));
        }

        // mavenFiles 下载到库目录但不进 classpath，对应 IsPresent=false。
        foreach (var library in patch.MavenFiles ?? [])
        {
            if (!TryConvertLibrary(library, true, out var converted, out var diagnostic))
            {
                diagnostics.Add(new(LaunchPlanDiagnostic.Kind.Warning, diagnostic!));
                continue;
            }

            operations.Add(new LaunchPlanDocument.AddLibraryOperation(converted!));
        }

        return new(new() { Operations = operations }, diagnostics);
    }

    public static byte[] Serialize(LaunchPlanDocument document) =>
        Encoding.UTF8.GetBytes(JsonSerializer.Serialize(document, FileHelper.SerializerOptions));

    // 将 MMC 的多种 url 约定规范化为原生计划的唯一含义：可直接抓取的完整地址，或指向层内文件的相对引用。
    // 优先链与上游一致：MMC-hint:local > downloads.artifact > MMC-absoluteUrl > url（仓库根）> 官方默认仓库。
    // WARNING: 导入端的引用收集与 operation 转换必须共用本函数。各自再写一遍优先链，两边对
    //  “哪些库指向包内文件”的结论会逐步漂移，表现为计划引用了一个未被解压的文件。
    public static bool TryResolveLibrary(
        MmcPatch.MmcLibrary library,
        [NotNullWhen(true)] out ResolvedLibrary? resolved,
        out string? diagnostic)
    {
        resolved = null;
        diagnostic = null;
        LockData.Library.Identity identity;
        try
        {
            identity = LibraryHelper.ParseIdentity(library.Name);
        }
        catch (Exception e) when (e is FormatException or NotSupportedException)
        {
            diagnostic = $"MMC library '{library.Name}' has an unsupported identity.";
            return false;
        }

        // hint 是显式意图声明，背景是“文件已随包携带”，优先于顺带写下的 downloads 元数据。
        // WARNING: 上游的本地库目录是扁平的（只按构件文件名寻址），不是 maven 目录结构；
        //  用 RelativePathOf 会拼出包里不存在的多层路径。
        if (library.Hint is MultiMcHelper.LIBRARY_HINT_LOCAL)
        {
            var fileName = library.FileName ?? LibraryHelper.FileNameOf(identity);
            resolved = new(identity,
                           new($"{MultiMcHelper.PACK_LIBRARIES_DIR}/{fileName}", UriKind.Relative),
                           null);
            return true;
        }

        if (library.Downloads?.Artifact is { } artifact)
        {
            resolved = new(identity, artifact.Url, FileHash.FromSha1(artifact.Sha1));
            return true;
        }

        if ((library.AbsoluteUrl ?? library.LegacyAbsoluteUrl) is { } absolute)
        {
            resolved = new(identity, absolute, null);
            return true;
        }

        resolved = new(identity,
                       LibraryHelper.ResolveAgainstRepository(library.RepositoryUrl ?? DEFAULT_LIBRARY_REPOSITORY,
                                                              identity),
                       null);
        return true;
    }

    // WARNING: 转换必须在导入边界完成。把 maven 仓库根原样写进原生计划会让下载抽到目录
    //  列表页，而该声明无 sha1、hash 校验形同虚设，坏文件会静默进共享缓存。
    private static bool TryConvertLibrary(
        MmcPatch.MmcLibrary library,
        bool mavenOnly,
        out LockData.Library? converted,
        out string? diagnostic)
    {
        converted = null;
        if (!TryResolveLibrary(library, out var resolved, out diagnostic))
        {
            return false;
        }

        converted = new(resolved.Identity,
                        resolved.Url,
                        resolved.Hash,
                        library.Natives is not null,
                        !mavenOnly);
        return true;
    }

    private static IReadOnlyList<string> SplitArguments(string value)
    {
        var result = new List<string>();
        var builder = new StringBuilder();
        var quote = '\0';
        var escaping = false;
        foreach (var ch in value)
        {
            if (escaping)
            {
                builder.Append(ch);
                escaping = false;
                continue;
            }

            if (ch == '\\' && quote != '\0')
            {
                escaping = true;
                continue;
            }

            if (quote != '\0')
            {
                if (ch == quote)
                {
                    quote = '\0';
                }
                else
                {
                    builder.Append(ch);
                }

                continue;
            }

            if (ch is '\'' or '"')
            {
                quote = ch;
            }
            else if (char.IsWhiteSpace(ch))
            {
                if (builder.Length > 0)
                {
                    result.Add(builder.ToString());
                    builder.Clear();
                }
            }
            else
            {
                builder.Append(ch);
            }
        }

        if (escaping)
        {
            builder.Append('\\');
        }

        if (builder.Length > 0)
        {
            result.Add(builder.ToString());
        }

        return result;
    }

    public sealed record ConversionResult(LaunchPlanDocument Document, IReadOnlyList<LaunchPlanDiagnostic> Diagnostics);

    // Url 可能是完整下载地址，也可能是相对引用（指向随包携带、将被解压到计划层内的文件）。
    public sealed record ResolvedLibrary(LockData.Library.Identity Identity, Uri Url, FileHash? Hash);
}
