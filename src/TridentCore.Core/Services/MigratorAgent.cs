using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
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

        // Gather every identifiable file across all selected instances first, then batch-identify once
        // so RepositoryAgent's internal chunking/concurrency is fully exploited — per-instance requests
        // would squander that for instances holding only a handful of mods.
        var identifiable = GatherIdentifiableFiles(list, cancellationToken);
        var hitFiles = new HashSet<string>(StringComparer.Ordinal);
        var packagesByInstance = new Dictionary<LauncherInstance, List<Package>>();

        if (identifiable.Count > 0)
        {
            var results = await repository
                                .IdentifyBatchAsync(identifiable.Select(x => x.File))
                                .ConfigureAwait(false);

            for (var i = 0; i < identifiable.Count && i < results.Count; i++)
            {
                if (results[i] is not { } pkg)
                {
                    continue;
                }

                var (instance, file) = identifiable[i];
                hitFiles.Add(file);
                if (!packagesByInstance.TryGetValue(instance, out var bucket))
                {
                    bucket = [];
                    packagesByInstance[instance] = bucket;
                }

                bucket.Add(pkg);
            }
        }

        for (var i = 0; i < list.Count; i++)
        {
            // Cancellation honours the instance boundary: the in-flight instance finishes its file copy,
            // and migration stops before starting the next one. Already-completed instances are kept.
            if (cancellationToken.IsCancellationRequested)
            {
                break;
            }

            var instance = list[i];
            var displayName = instance.Name ?? instance.Key;
            try
            {
                if (instance.MinecraftVersion is null)
                {
                    throw new InvalidOperationException("Instance has no resolvable Minecraft version");
                }

                progress?.Report(new MigrateProgress
                {
                    CurrentPhase = MigrateProgress.Phase.Transferring,
                    InstanceName = displayName,
                    InstanceIndex = i + 1,
                    InstanceTotal = list.Count,
                    Percent = 0
                });

                var reservedKey = profiles.RequestKey(instance.Key);
                var finalKey = reservedKey.Key;
                packagesByInstance.TryGetValue(instance, out var instancePackages);
                profiles.Add(reservedKey, BuildProfile(instance, instancePackages));

                var buildDir = PathDef.Default.DirectoryOfBuild(finalKey);
                await TransferFilesAsync(instance.RuntimeDirectory,
                                         buildDir,
                                         hitFiles,
                                         displayName,
                                         i + 1,
                                         list.Count,
                                         progress,
                                         cancellationToken).ConfigureAwait(false);

                entries.Add(new MigrateResult.Entry(displayName, true));
                logger.LogInformation("Migrated {Name} as {Key}", displayName, finalKey);
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
    // Cancellation is NOT checked per-file — interruption honours the instance boundary in MigrateAsync
    // so a started instance always lands complete or not at all, never half-copied.
    private static async Task TransferFilesAsync(
        string sourceDir,
        string targetDir,
        IReadOnlySet<string> skip,
        string displayName,
        int instanceIndex,
        int instanceTotal,
        IProgress<MigrateProgress>? progress,
        CancellationToken cancellationToken)
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

            File.Copy(file, target, overwrite: true);

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
}
