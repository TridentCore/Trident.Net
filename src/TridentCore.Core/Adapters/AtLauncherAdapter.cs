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

    // loaderVersion.type (case-insensitive) → loader identity. LegacyFabric maps to Fabric. Paper and
    // Purpur are server stacks rather than client loaders, so they are left unmapped (null) and such
    // instances migrate as loader-less rather than with a wrong loader.
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

        // ATLauncher is portable by default — its data directory is wherever the executable lives — so
        // there is no fixed OS-standard location. Probe the conventional roots (older installs and some
        // wrappers still use AppData/~/Library/.../ATLauncher) and the Flatpak sandbox path; otherwise
        // return null and let the user point at it manually.
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
            // The top-level `id` (inherited from Mojang's MinecraftVersion) is the Minecraft version.
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

        // ATLauncher deploys directly into the instance folder — no nested .minecraft layer.
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
            IdentifiableSubdirs = [.. IDENTIFIABLE_SUBDIRS.Where(d => Directory.Exists(Path.Combine(instanceDir, d)))]
        };
    }

    private static string? ResolveLoader(AtLauncherInstance.LoaderVersion? loaderVersion)
    {
        if (loaderVersion is null || string.IsNullOrEmpty(loaderVersion.Type) || string.IsNullOrEmpty(loaderVersion.Version))
        {
            return null;
        }

        return LOADER_BY_TYPE.TryGetValue(loaderVersion.Type, out var identity)
            ? LoaderHelper.ToLurl(identity, loaderVersion.Version)
            : null;
    }
}
