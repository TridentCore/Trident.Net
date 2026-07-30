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

    // MultiMC, PolyMC and PrismLauncher share one instance format (mmc-pack.json + instance.cfg +
    // .minecraft/) since PrismLauncher/PolyMC are forks of MultiMC5. One adapter serves both brands;
    // only the data-directory name differs, resolved per kind below.
    public IReadOnlyList<LauncherKind> SupportedKinds { get; } = [LauncherKind.MultiMc, LauncherKind.PrismLauncher];

    public string? DefaultDataDirectory(LauncherKind kind) => kind switch
    {
        LauncherKind.MultiMc => LauncherDataDirHelper.LocateUnderConventional("MultiMC", "PolyMC"),
        LauncherKind.PrismLauncher => LauncherDataDirHelper.LocateUnderConventional("PrismLauncher"),
        _ => null
    };

    public async Task<IReadOnlyList<LauncherInstance>> ScanAsync(string rootDir, CancellationToken cancellationToken = default)
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
            // Skip launcher-internal/hidden directories like .tmp that PrismLauncher creates in instances/.
            if (Path.GetFileName(instanceDir).StartsWith('.'))
            {
                continue;
            }

            results.Add(await ScanInstanceAsync(instanceDir, logger, cancellationToken).ConfigureAwait(false));
        }

        return results;
    }

    private static async Task<LauncherInstance> ScanInstanceAsync(string instanceDir, ILogger? logger, CancellationToken cancellationToken)
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
            IdentifiableSubdirs = [.. IDENTIFIABLE_SUBDIRS.Where(d => Directory.Exists(Path.Combine(runtimeDir, d)))]
        };
    }

    // instance.cfg is an INI-ish key=value file; read every line into a lookup so both the name and a
    // possible InstanceDir override are available without a second pass.
    private static async Task<Dictionary<string, string>> ReadInstanceCfgAsync(string instanceDir, CancellationToken cancellationToken)
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
            // best-effort; caller falls back to defaults
        }

        return cfg;
    }

    // Resolve the runtime directory: honour a per-instance InstanceDir override (relative or absolute),
    // otherwise probe the conventional names, falling back to .minecraft even when absent so the caller
    // can report it rather than silently treating the instance as having no files.
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
