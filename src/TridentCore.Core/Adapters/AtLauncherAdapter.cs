using System.Text.Json;
using Microsoft.Extensions.Logging;
using TridentCore.Abstractions.Adapters;
using TridentCore.Abstractions.Utilities;
using TridentCore.Core.Models.AtLauncher;
using TridentCore.Core.Utilities;

namespace TridentCore.Core.Adapters;

public class AtLauncherAdapter(ILogger<AtLauncherAdapter>? logger = null) : ILauncherAdapter
{
    private const string INSTANCE_FILE = "instance.json";
    private const string INSTANCES_FOLDER = "instances";
    private const string FLATPAK_RELATIVE = ".var/app/com.atlauncher.ATLauncher/data";

    private static readonly string[] IDENTIFIABLE_SUBDIRS = ["mods", "resourcepacks", "shaderpacks", "jarmods"];

    // NOTE: loaderVersion.type（不区分大小写）→ loader 标识。LegacyFabric 映射 Fabric；Paper/Purpur
    //  是服务端栈而非客户端 loader，保持未映射（null），此类实例以无 loader 迁移而非错误 loader。
    private static readonly Dictionary<string, string> LOADER_BY_TYPE = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Forge"] = LoaderHelper.LOADERID_FORGE,
        ["NeoForge"] = LoaderHelper.LOADERID_NEOFORGE,
        ["Fabric"] = LoaderHelper.LOADERID_FABRIC,
        ["Quilt"] = LoaderHelper.LOADERID_QUILT,
        ["LegacyFabric"] = LoaderHelper.LOADERID_FABRIC
    };

    public IReadOnlyList<LauncherKind> SupportedKinds { get; } = [LauncherKind.AtLauncher];

    public string? DefaultDataDirectory(LauncherKind kind)
    {
        if (kind != LauncherKind.AtLauncher)
        {
            return null;
        }

        // NOTE: ATLauncher 默认便携——数据目录在可执行文件旁，无固定系统位。
        //  探测常规根（旧安装/部分包装器仍用 AppData/~/Library/.../ATLauncher）与 Flatpak 沙箱路径，
        //  否则返回 null 由用户手动指定。
        var conventional = LauncherDataDirHelper.LocateUnderConventional("ATLauncher");
        if (conventional is not null)
        {
            return conventional;
        }

        var profile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        if (!string.IsNullOrEmpty(profile))
        {
            var flatpak = Path.Combine(profile, FLATPAK_RELATIVE);
            if (Directory.Exists(flatpak))
            {
                return flatpak;
            }
        }

        return null;
    }

    public async Task<IReadOnlyList<LauncherInstance>> ScanAsync(
        string rootDir,
        CancellationToken cancellationToken = default)
    {
        var instancesDir = Path.Combine(rootDir, INSTANCES_FOLDER);
        if (!Directory.Exists(instancesDir))
        {
            return [];
        }

        var results = new List<LauncherInstance>();
        foreach (var instanceDir in Directory.EnumerateDirectories(instancesDir))
        {
            cancellationToken.ThrowIfCancellationRequested();
            results.Add(await ScanInstanceAsync(instanceDir, logger, cancellationToken).ConfigureAwait(false));
        }

        return results;
    }

    private static async Task<LauncherInstance> ScanInstanceAsync(
        string instanceDir,
        ILogger? logger,
        CancellationToken cancellationToken)
    {
        var key = Path.GetFileName(instanceDir);
        var file = Path.Combine(instanceDir, INSTANCE_FILE);

        AtLauncherInstance? data = null;
        CorruptReason? corruptReason = null;
        if (File.Exists(file))
        {
            try
            {
                await using var stream = File.OpenRead(file);
                data = await JsonSerializer
                            .DeserializeAsync<AtLauncherInstance>(stream, JsonSerializerOptions.Web, cancellationToken)
                            .ConfigureAwait(false);
                if (data is null)
                {
                    corruptReason = CorruptReason.PackFileMalformed;
                }
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                logger?.LogWarning(ex, "Failed to parse {File} for {Key}", INSTANCE_FILE, key);
                corruptReason = CorruptReason.PackFileMalformed;
            }
        }
        else
        {
            corruptReason = CorruptReason.PackFileMissing;
        }

        string? version = null;
        string? loader = null;
        var name = key;
        if (data is not null)
        {
            // NOTE: 顶层 `id`（继承自 Mojang 的 MinecraftVersion）即 Minecraft 版本。
            version = data.Id;
            if (string.IsNullOrEmpty(version))
            {
                corruptReason ??= CorruptReason.MinecraftComponentMissing;
            }

            var launcher = data.Launcher;
            if (!string.IsNullOrEmpty(launcher?.Name))
            {
                name = launcher.Name;
            }

            loader = ResolveLoader(launcher?.LoaderVersion);
        }

        // NOTE: ATLauncher 直接部署进实例文件夹——无嵌套 .minecraft 层。
        return new()
        {
            Kind = LauncherKind.AtLauncher,
            Key = key,
            HomeDirectory = instanceDir,
            RuntimeDirectory = instanceDir,
            Name = name,
            MinecraftVersion = version,
            Loader = loader,
            CorruptReason = corruptReason,
            IdentifiableSubdirs =
            [
                .. IDENTIFIABLE_SUBDIRS.Where(d => Directory.Exists(Path.Combine(instanceDir, d)))
            ]
        };
    }

    private static string? ResolveLoader(AtLauncherInstance.LoaderVersion? loaderVersion)
    {
        if (loaderVersion is null
         || string.IsNullOrEmpty(loaderVersion.Type)
         || string.IsNullOrEmpty(loaderVersion.Version))
        {
            return null;
        }

        return LOADER_BY_TYPE.TryGetValue(loaderVersion.Type, out var identity)
                   ? LoaderHelper.ToLurl(identity, loaderVersion.Version)
                   : null;
    }
}
