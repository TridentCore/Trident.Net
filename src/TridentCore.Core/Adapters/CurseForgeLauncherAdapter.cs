using System.Text.Json;
using Microsoft.Extensions.Logging;
using TridentCore.Abstractions.Adapters;
using TridentCore.Abstractions.Utilities;
using TridentCore.Core.Models.CurseForgeLauncher;

namespace TridentCore.Core.Adapters;

public class CurseForgeLauncherAdapter(ILogger<CurseForgeLauncherAdapter>? logger = null) : ILauncherAdapter
{
    private const string INSTANCE_FILE = "minecraftinstance.json";
    private const string INSTANCES_FOLDER = "Instances";
    private const string MINECRAFT_FOLDER = "minecraft";

    private static readonly string[] IDENTIFIABLE_SUBDIRS = ["mods", "resourcepacks", "shaderpacks"];

    // NOTE: baseModLoader.type → loader 标识。Cauldron(2)/LiteLoader(3) 是遗留类型未映射，
    //  此类实例回退为无 loader 而非错误 loader。
    private static readonly Dictionary<int, string> LOADER_BY_TYPE = new()
    {
        [1] = LoaderHelper.LOADERID_FORGE,
        [4] = LoaderHelper.LOADERID_FABRIC,
        [5] = LoaderHelper.LOADERID_QUILT,
        [6] = LoaderHelper.LOADERID_NEOFORGE
    };

    public IReadOnlyList<LauncherKind> SupportedKinds { get; } = [LauncherKind.CurseForgeApp];

    public string? DefaultDataDirectory(LauncherKind kind)
    {
        if (kind != LauncherKind.CurseForgeApp)
        {
            return null;
        }

        // NOTE: CurseForge App 存于用户目录而非 AppData——独立版在 ~/curseforge，
        //  Overwolf 托管版在 ~/Overwolf/CurseForge，两者都有含 Instances/ 的 minecraft/ 根。预填存在者。
        var profile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        if (string.IsNullOrEmpty(profile))
        {
            return null;
        }

        foreach (var candidate in new[] { "curseforge", Path.Combine("Overwolf", "CurseForge") })
        {
            var minecraft = Path.Combine(profile, candidate, MINECRAFT_FOLDER);
            if (Directory.Exists(Path.Combine(minecraft, INSTANCES_FOLDER)))
            {
                return minecraft;
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

        CurseForgeInstance? data = null;
        CorruptReason? corruptReason = null;
        if (File.Exists(file))
        {
            try
            {
                await using var stream = File.OpenRead(file);
                data = await JsonSerializer
                            .DeserializeAsync<CurseForgeInstance>(stream, JsonSerializerOptions.Web, cancellationToken)
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
        if (data is not null)
        {
            version = data.GameVersion;
            if (string.IsNullOrEmpty(version))
            {
                corruptReason ??= CorruptReason.MinecraftComponentMissing;
            }

            loader = ResolveLoader(data.BaseModLoader);
        }

        // NOTE: 对 CurseForge，实例文件夹即游戏目录——mods/、saves/ 等在根上。
        return new()
        {
            Kind = LauncherKind.CurseForgeApp,
            Key = key,
            HomeDirectory = instanceDir,
            RuntimeDirectory = instanceDir,
            Name = string.IsNullOrEmpty(data?.Name) ? key : data.Name,
            MinecraftVersion = version,
            Loader = loader,
            CorruptReason = corruptReason,
            IdentifiableSubdirs =
            [
                .. IDENTIFIABLE_SUBDIRS.Where(d => Directory.Exists(Path.Combine(instanceDir, d)))
            ]
        };
    }

    private static string? ResolveLoader(CurseForgeInstance.ModLoader? baseModLoader)
    {
        if (baseModLoader is null || string.IsNullOrEmpty(baseModLoader.Name))
        {
            return null;
        }

        if (!LOADER_BY_TYPE.TryGetValue(baseModLoader.Type, out var identity))
        {
            return null;
        }

        var name = baseModLoader.Name;
        var dash = name.IndexOf('-');
        if (dash < 0 || dash >= name.Length - 1)
        {
            return null;
        }

        // NOTE: 名称形如 forge-<v>、fabric-<loaderVer>-<mcVer>、quilt-<v>、neoforge-<v>。
        //  Fabric 的 loader 版本在首个与第二个连字符之间，其余取首个连字符之后。
        var version = identity == LoaderHelper.LOADERID_FABRIC ? ExtractFabricVersion(name, dash) : name[(dash + 1)..];

        return string.IsNullOrEmpty(version) ? null : LoaderHelper.ToLurl(identity, version);
    }

    private static string ExtractFabricVersion(string name, int firstDash)
    {
        var rest = name[(firstDash + 1)..];
        var next = rest.IndexOf('-');
        return next < 0 ? rest : rest[..next];
    }
}
