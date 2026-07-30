using Microsoft.Extensions.Logging;
using TridentCore.Abstractions;
using TridentCore.Abstractions.Adapters;
using TridentCore.Abstractions.FileModels;
using TridentCore.Abstractions.Repositories.Resources;
using TridentCore.Abstractions.Utilities;

namespace TridentCore.Core.Services;

public class MigratorAgent(
    IEnumerable<ILauncherAdapter> adapters,
    RepositoryAgent repository,
    ProfileManager profiles,
    ILogger<MigratorAgent> logger)
{
    private readonly Dictionary<LauncherKind, ILauncherAdapter> _adapters = BuildKindIndex(adapters);

    public LauncherKind[] SupportedKinds => _adapters.Keys.ToArray();

    public string? DefaultDataDirectory(LauncherKind kind) =>
        _adapters.TryGetValue(kind, out var adapter) ? adapter.DefaultDataDirectory(kind) : null;

    public Task<IReadOnlyList<LauncherInstance>> ScanAsync(
        LauncherKind kind,
        string rootDir,
        CancellationToken cancellationToken = default)
    {
        if (!_adapters.TryGetValue(kind, out var adapter))
        {
            throw new ArgumentException($"No launcher adapter registered for {kind}", nameof(kind));
        }

        return adapter.ScanAsync(rootDir, cancellationToken);
    }

    public async Task<MigrateResult> MigrateAsync(
        IEnumerable<LauncherInstance> instances,
        IProgress<MigrateProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        var list = instances.ToList();
        var entries = new List<MigrateResult.Entry>();
        if (list.Count == 0)
        {
            return new MigrateResult(entries);
        }

        progress?.Report(new MigrateProgress { CurrentPhase = MigrateProgress.Phase.Identifying });

        var identifiable = GatherIdentifiableFiles(list, cancellationToken);
        var hitFiles = new HashSet<string>(StringComparer.Ordinal);
        var packagesByInstance = new Dictionary<LauncherInstance, List<Package>>();

        if (identifiable.Count > 0)
        {
            var results = await repository
                                .IdentifyBatchAsync(identifiable.Select(x => x.File), cancellationToken)
                                .ConfigureAwait(false);

            foreach (var (instance, file) in identifiable)
            {
                if (results.TryGetValue(file, out var pkg) && pkg is not null)
                {
                    hitFiles.Add(file);
                    if (!packagesByInstance.TryGetValue(instance, out var bucket))
                    {
                        bucket = [];
                        packagesByInstance[instance] = bucket;
                    }

                    bucket.Add(pkg);
                }
            }
        }

        for (var i = 0; i < list.Count; i++)
        {
            // NOTE: cancellation honours the instance boundary — the in-flight instance finishes its file
            //  copy and migration stops before the next one, so a started instance always lands whole or
            //  not at all. Already-completed instances are kept.
            if (cancellationToken.IsCancellationRequested)
            {
                break;
            }

            var instance = list[i];
            var displayName = instance.Name ?? instance.Key;
            try
            {
                if (instance.CorruptReason is not null)
                {
                    throw new InvalidOperationException($"Instance is corrupt: {instance.CorruptReason}");
                }

                progress?.Report(new MigrateProgress
                {
                    CurrentPhase = MigrateProgress.Phase.Transferring,
                    InstanceName = displayName,
                    InstanceIndex = i + 1,
                    InstanceTotal = list.Count,
                    Percent = 0
                });

                // NOTE: files land in build/ first and the profile is registered only after a full
                //  transfer, so a failed copy leaves no profile behind. On failure the reserved key is
                //  released and the partial build directory removed so retries start clean.
                var reservedKey = profiles.RequestKey(instance.Key);
                var registered = false;
                try
                {
                    var buildDir = PathDef.Default.DirectoryOfBuild(reservedKey.Key);
                    packagesByInstance.TryGetValue(instance, out var instancePackages);
                    await TransferFilesAsync(instance.RuntimeDirectory,
                                             buildDir,
                                             hitFiles,
                                             displayName,
                                             i + 1,
                                             list.Count,
                                             progress).ConfigureAwait(false);
                    profiles.Add(reservedKey, BuildProfile(instance, instancePackages));
                    registered = true;
                    entries.Add(new MigrateResult.Entry(displayName, true));
                    logger.LogInformation("Migrated {Name} as {Key}", displayName, reservedKey.Key);
                }
                finally
                {
                    if (!registered)
                    {
                        reservedKey.Dispose();
                        BestEffortDelete(PathDef.Default.DirectoryOfBuild(reservedKey.Key));
                    }
                }
            }
            catch (Exception ex)
            {
                entries.Add(new MigrateResult.Entry(displayName, false, ex.Message));
                logger.LogError(ex, "Failed to migrate {Name}", displayName);
            }
        }

        return new MigrateResult(entries);
    }

    private static Dictionary<LauncherKind, ILauncherAdapter> BuildKindIndex(IEnumerable<ILauncherAdapter> adapters)
    {
        var index = new Dictionary<LauncherKind, ILauncherAdapter>();
        foreach (var adapter in adapters)
        {
            foreach (var kind in adapter.SupportedKinds)
            {
                index[kind] = adapter;
            }
        }

        return index;
    }

    private static List<(LauncherInstance Instance, string File)> GatherIdentifiableFiles(
        IReadOnlyList<LauncherInstance> instances,
        CancellationToken cancellationToken)
    {
        var collected = new List<(LauncherInstance, string)>();
        foreach (var instance in instances)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!Directory.Exists(instance.RuntimeDirectory))
            {
                continue;
            }

            foreach (var subdir in instance.IdentifiableSubdirs)
            {
                var dir = Path.Combine(instance.RuntimeDirectory, subdir);
                if (!Directory.Exists(dir))
                {
                    continue;
                }

                foreach (var file in Directory.EnumerateFiles(dir, "*.*", SearchOption.TopDirectoryOnly))
                {
                    var ext = Path.GetExtension(file);
                    if (ext.Equals(".jar", StringComparison.OrdinalIgnoreCase)
                        || ext.Equals(".zip", StringComparison.OrdinalIgnoreCase))
                    {
                        collected.Add((instance, file));
                    }
                }
            }
        }

        return collected;
    }

    private static Profile BuildProfile(LauncherInstance instance, IReadOnlyList<Package>? packages)
    {
        var setup = new Profile.Rice
        {
            Version = instance.MinecraftVersion!,
            Loader = instance.Loader,
            Packages = (packages ?? [])
                       .Select(p => new Profile.Rice.Entry
                       {
                           Pref = PackageHelper.ToPref(p),
                           Enabled = true
                       })
                       .ToList()
        };
        return new Profile { Name = instance.Name ?? instance.Key, Setup = setup };
    }

    // Copies the whole runtime tree into build/, skipping files that were turned into package refs.
    private static async Task TransferFilesAsync(
        string sourceDir,
        string targetDir,
        IReadOnlySet<string> skip,
        string displayName,
        int instanceIndex,
        int instanceTotal,
        IProgress<MigrateProgress>? progress)
    {
        if (!Directory.Exists(sourceDir))
        {
            return;
        }

        Directory.CreateDirectory(targetDir);

        var files = Directory.EnumerateFiles(sourceDir, "*.*", SearchOption.AllDirectories)
                             .Where(f => !skip.Contains(f))
                             .ToList();

        var lastReportedPercent = -1;
        for (var i = 0; i < files.Count; i++)
        {
            var file = files[i];
            var relative = Path.GetRelativePath(sourceDir, file);
            var target = Path.Combine(targetDir, relative);
            var targetParent = Path.GetDirectoryName(target);
            if (!string.IsNullOrEmpty(targetParent))
            {
                Directory.CreateDirectory(targetParent);
            }

            await using var source = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.Read,
                                                    bufferSize: 81920, useAsync: true);
            await using var dest = new FileStream(target, FileMode.Create, FileAccess.Write, FileShare.None,
                                                  bufferSize: 81920, useAsync: true);
            await source.CopyToAsync(dest);

            var percent = (int)Math.Round((double)(i + 1) / files.Count * 100);
            if (percent != lastReportedPercent || i == files.Count - 1)
            {
                lastReportedPercent = percent;
                progress?.Report(new MigrateProgress
                {
                    CurrentPhase = MigrateProgress.Phase.Transferring,
                    InstanceName = displayName,
                    InstanceIndex = instanceIndex,
                    InstanceTotal = instanceTotal,
                    Percent = percent / 100.0
                });
            }
        }
    }

    private static void BestEffortDelete(string dir)
    {
        try
        {
            if (Directory.Exists(dir))
            {
                Directory.Delete(dir, recursive: true);
            }
        }
        catch
        {
            // best-effort cleanup; the failure that triggered this is the one reported
        }
    }
}
