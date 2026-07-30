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

    // baseModLoader.type → loader identity. Cauldron(2) and LiteLoader(3) are legacy and unmapped,
    // so such instances fall back to no loader rather than a wrong one.
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

        // CurseForge App stores under the user profile rather than AppData: the standalone app under
        // ~/curseforge, the Overwolf-hosted build under ~/Overwolf/CurseForge, both with a minecraft/
        // root holding Instances/. Prefill whichever exists.
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

    public async Task<IReadOnlyList<LauncherInstance>> ScanAsync(string rootDir, CancellationToken cancellationToken = default)
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

        // The instance folder IS the game directory for CurseForge — mods/, saves/ etc. sit at its root.
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
            IdentifiableSubdirs = [.. IDENTIFIABLE_SUBDIRS.Where(d => Directory.Exists(Path.Combine(instanceDir, d)))]
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

        // name is forge-<v>, fabric-<loaderVer>-<mcVer>, quilt-<v>, or neoforge-<v>. For Fabric the
        // loader version is the segment between the first and second dashes; for the rest it is
        // everything after the first dash.
        var version = identity == LoaderHelper.LOADERID_FABRIC
            ? ExtractFabricVersion(name, dash)
            : name[(dash + 1)..];

        return string.IsNullOrEmpty(version) ? null : LoaderHelper.ToLurl(identity, version);
    }

    private static string ExtractFabricVersion(string name, int firstDash)
    {
        var rest = name[(firstDash + 1)..];
        var next = rest.IndexOf('-');
        return next < 0 ? rest : rest[..next];
    }
}
