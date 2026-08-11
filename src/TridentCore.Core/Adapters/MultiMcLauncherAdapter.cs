using System.Text.Json;
using Microsoft.Extensions.Logging;
using TridentCore.Abstractions.Adapters;
using TridentCore.Abstractions.Utilities;
using TridentCore.Core.Models.MultiMcPack;
using TridentCore.Core.Utilities;

namespace TridentCore.Core.Adapters;

public class MultiMcLauncherAdapter(ILogger<MultiMcLauncherAdapter>? logger = null) : ILauncherAdapter
{
    private static readonly string[] IDENTIFIABLE_SUBDIRS = ["mods", "resourcepacks", "shaderpacks"];
    private static readonly string[] DEFAULT_RUNTIME_CANDIDATES = [".minecraft", "minecraft"];

    // NOTE: MultiMC/PolyMC/PrismLauncher 共享同一实例格式（mmc-pack.json + instance.cfg + .minecraft/），
    //  一个适配器服务两个品牌，仅数据目录名不同，按下文 kind 解析。
    public IReadOnlyList<LauncherKind> SupportedKinds { get; } = [LauncherKind.MultiMc, LauncherKind.PrismLauncher];

    public string? DefaultDataDirectory(LauncherKind kind) =>
        kind switch
        {
            LauncherKind.MultiMc => LauncherDataDirHelper.LocateUnderConventional("MultiMC", "PolyMC"),
            LauncherKind.PrismLauncher => LauncherDataDirHelper.LocateUnderConventional("PrismLauncher"),
            _ => null
        };

    public async Task<IReadOnlyList<LauncherInstance>> ScanAsync(
        string rootDir,
        CancellationToken cancellationToken = default)
    {
        var instancesDir = Path.Combine(rootDir, "instances");
        if (!Directory.Exists(instancesDir))
        {
            return [];
        }

        var results = new List<LauncherInstance>();
        foreach (var instanceDir in Directory.EnumerateDirectories(instancesDir))
        {
            cancellationToken.ThrowIfCancellationRequested();
            // NOTE: 跳过 .tmp 等启动器内部/隐藏目录（PrismLauncher 会在 instances/ 下创建）。
            if (Path.GetFileName(instanceDir).StartsWith('.'))
            {
                continue;
            }

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

        MmcPack? pack = null;
        CorruptReason? corruptReason = null;
        var packFile = Path.Combine(instanceDir, MultiMcHelper.PACK_INDEX_FILE_NAME);
        if (File.Exists(packFile))
        {
            try
            {
                await using var stream = File.OpenRead(packFile);
                pack = await JsonSerializer
                            .DeserializeAsync<MmcPack>(stream, JsonSerializerOptions.Web, cancellationToken)
                            .ConfigureAwait(false);
                if (pack is null)
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
                logger?.LogWarning(ex, "Failed to parse {Pack} for {Key}", MultiMcHelper.PACK_INDEX_FILE_NAME, key);
                corruptReason = CorruptReason.PackFileMalformed;
            }
        }
        else
        {
            corruptReason = CorruptReason.PackFileMissing;
        }

        var cfg = await ReadInstanceCfgAsync(instanceDir, cancellationToken).ConfigureAwait(false);
        var name = cfg.GetValueOrDefault("name");

        string? version = null;
        string? loader = null;
        if (pack is not null)
        {
            version = pack.Components.FirstOrDefault(c => c.Uid == MultiMcHelper.UID_MINECRAFT)?.Version;
            if (version is null)
            {
                corruptReason ??= CorruptReason.MinecraftComponentMissing;
            }

            foreach (var component in pack.Components)
            {
                if (MultiMcHelper.UidToLoaderMappings.TryGetValue(component.Uid, out var loaderId))
                {
                    loader = LoaderHelper.ToLurl(loaderId, component.Version);
                    break;
                }
            }
        }

        var runtimeDir = ResolveRuntimeDir(instanceDir, cfg.GetValueOrDefault("InstanceDir"));
        if (!Directory.Exists(runtimeDir))
        {
            logger?.LogWarning("Runtime directory not found for {Key}: {Path}", key, runtimeDir);
        }

        return new()
        {
            Kind = LauncherKind.MultiMc,
            Key = key,
            HomeDirectory = instanceDir,
            Name = string.IsNullOrEmpty(name) ? key : name,
            MinecraftVersion = version,
            Loader = loader,
            CorruptReason = corruptReason,
            RuntimeDirectory = runtimeDir,
            IdentifiableSubdirs =
            [
                .. IDENTIFIABLE_SUBDIRS.Where(d => Directory.Exists(Path.Combine(runtimeDir, d)))
            ]
        };
    }

    // NOTE: instance.cfg 是 INI 式 key=value 文件；逐行读入查找表，名称与可能的
    //  InstanceDir 覆盖一次读齐，无需第二遍。
    private static async Task<Dictionary<string, string>> ReadInstanceCfgAsync(
        string instanceDir,
        CancellationToken cancellationToken)
    {
        var cfg = new Dictionary<string, string>();
        var cfgFile = Path.Combine(instanceDir, MultiMcHelper.PACK_INSTANCE_CFG);
        if (!File.Exists(cfgFile))
        {
            return cfg;
        }

        try
        {
            using var reader = new StreamReader(cfgFile);
            while (await reader.ReadLineAsync(cancellationToken).ConfigureAwait(false) is { } line)
            {
                var eq = line.IndexOf('=');
                if (eq > 0)
                {
                    cfg[line[..eq]] = line[(eq + 1)..];
                }
            }
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch
        {
            // NOTE: 尽力而为；调用方回退默认值。
        }

        return cfg;
    }

    // NOTE: 解析运行目录——尊重每实例的 InstanceDir 覆盖（相对或绝对），否则探测常规名，
    //  缺席时也回退 .minecraft，让调用方上报而非静默当作无文件实例。
    private static string ResolveRuntimeDir(string instanceDir, string? instanceDirCfg)
    {
        if (!string.IsNullOrEmpty(instanceDirCfg))
        {
            return Path.IsPathRooted(instanceDirCfg) ? instanceDirCfg : Path.Combine(instanceDir, instanceDirCfg);
        }

        foreach (var candidate in DEFAULT_RUNTIME_CANDIDATES)
        {
            var path = Path.Combine(instanceDir, candidate);
            if (Directory.Exists(path))
            {
                return path;
            }
        }

        return Path.Combine(instanceDir, DEFAULT_RUNTIME_CANDIDATES[0]);
    }
}
