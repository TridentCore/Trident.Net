using TridentCore.Abstractions;
using TridentCore.Abstractions.Utilities;
using TridentCore.Core.Exceptions;
using TridentCore.Core.Utilities;

namespace TridentCore.Core.Engines.Deploying;

public sealed class DeploymentDiffer
{
    public DeploymentPlan Diff(string key, DeploymentTarget target, CancellationToken token = default)
    {
        var plan = new DeploymentPlan();
        plan.Downloads.AddRange(target.Downloads);
        plan.Operations.AddRange(target.PreparationOperations);

        var build = PathDef.Default.DirectoryOfBuild(key);
        var oldImport = ProjectionManifestHelper.ReadImport(key);
        var oldImportPaths = oldImport.Files.Select(x => ProjectionManifestHelper.ResolveStoredPath(build, x))
            .ToHashSet(FileHelper.PathComparer);
        var oldPersistManifest = ProjectionManifestHelper.ReadPersist(key);
        var oldPersist = oldPersistManifest.Files.ToDictionary(
            x => ProjectionManifestHelper.ResolveStoredPath(build, x.Path), FileHelper.PathComparer);
        var newImportPaths = target.Projections
            .Where(x => x.Kind == DeploymentTarget.ProjectionKind.Import)
            .Select(x => x.Target)
            .ToHashSet(FileHelper.PathComparer);
        var handledRetired = new HashSet<string>(FileHelper.PathComparer);
        var persistEntries = new List<PersistProjectionManifest.FileEntry>();

        foreach (var projection in target.Projections.Where(x => x.Kind == DeploymentTarget.ProjectionKind.Package))
        {
            token.ThrowIfCancellationRequested();
            PlanPackage(projection, build, oldImportPaths);
        }

        foreach (var projection in target.Projections.Where(x => x.Kind == DeploymentTarget.ProjectionKind.Persist && x.Directory))
        {
            token.ThrowIfCancellationRequested();
            PlanPersistDirectory(projection, build, oldImportPaths, oldPersist, handledRetired, plan, token);
        }

        foreach (var projection in target.Projections.Where(x => x.Kind == DeploymentTarget.ProjectionKind.Persist && !x.Directory))
        {
            token.ThrowIfCancellationRequested();
            var ticks = PlanPersistFile(projection, build, oldImportPaths, oldPersist, handledRetired, plan);
            persistEntries.Add(new(ProjectionManifestHelper.ToStoredPath(build, projection.Target), ticks));
        }

        foreach (var retired in oldImportPaths.Where(x => !newImportPaths.Contains(x) && !handledRetired.Contains(x))
            .OrderByDescending(x => x.Length))
        {
            token.ThrowIfCancellationRequested();
            PlanRetiredImport(retired, build, plan);
        }

        foreach (var projection in target.Projections.Where(x => x.Kind == DeploymentTarget.ProjectionKind.Import)
            .OrderBy(x => x.Target, FileHelper.PathComparer))
        {
            token.ThrowIfCancellationRequested();
            PlanImportFile(projection, build, oldImportPaths, plan);
        }

        if (!Directory.Exists(build)) plan.Operations.Insert(0, new DeploymentPlan.CreateDirectory(build));
        plan.ImportManifest = new()
        {
            Files = [.. newImportPaths.Select(x => ProjectionManifestHelper.ToStoredPath(build, x))
                .OrderBy(x => x, FileHelper.PathComparer)]
        };
        plan.PersistManifest = new()
        {
            Files = [.. persistEntries.OrderBy(x => x.Path, FileHelper.PathComparer)]
        };
        AppendLinkOperations(
            build,
            target.Projections.Where(x => x.Kind != DeploymentTarget.ProjectionKind.Import),
            plan.Operations,
            token);
        plan.NeedsManifestCommit = !File.Exists(PathDef.Default.FileOfImportProjectionManifest(key))
            || !File.Exists(PathDef.Default.FileOfPersistProjectionManifest(key))
            || !oldImport.Files.SequenceEqual(plan.ImportManifest.Files, FileHelper.PathComparer)
            || !PersistManifestEquals(oldPersistManifest, plan.PersistManifest);
        return plan;
    }

    private static void PlanPackage(
        DeploymentTarget.Projection projection,
        string build,
        ISet<string> oldImport)
    {
        if (DeploymentFileHelper.HasLinkAtOrAbove(projection.Target, build)) return;
        if (File.Exists(projection.Target) && !oldImport.Contains(projection.Target)) throw Conflict(projection.Target);
        if (Directory.Exists(projection.Target) && !DirectoryCanBeReplaced(projection.Target, oldImport))
            throw Conflict(projection.Target);
    }

    private static void PlanImportFile(
        DeploymentTarget.Projection projection,
        string build,
        ISet<string> oldImport,
        DeploymentPlan plan)
    {
        if (DeploymentFileHelper.HasLinkAtOrAbove(projection.Target, build))
        {
            plan.Operations.Add(new DeploymentPlan.EnsureImportFile(projection.Source, projection.Target));
            return;
        }
        if (Directory.Exists(projection.Target) && !DirectoryCanBeReplaced(projection.Target, oldImport))
            throw Conflict(projection.Target);
        if (!File.Exists(projection.Target))
            plan.Operations.Add(new DeploymentPlan.EnsureImportFile(projection.Source, projection.Target));
    }

    private static void PlanRetiredImport(string path, string build, DeploymentPlan plan)
    {
        if (!DeploymentFileHelper.HasLinkAtOrAbove(path, build) && File.Exists(path))
            plan.Operations.Add(new DeploymentPlan.RemoveBuildFile(path));
    }

    private static long PlanPersistFile(
        DeploymentTarget.Projection projection,
        string build,
        ISet<string> oldImport,
        IReadOnlyDictionary<string, PersistProjectionManifest.FileEntry> oldPersist,
        ISet<string> handledRetired,
        DeploymentPlan plan)
    {
        var sourceTicks = File.GetLastWriteTimeUtc(projection.Source).Ticks;
        if (DeploymentFileHelper.HasLinkAtOrAbove(projection.Target, build)) return sourceTicks;
        if (Directory.Exists(projection.Target))
        {
            if (!DirectoryCanBeReplaced(projection.Target, oldImport)) throw Conflict(projection.Target);
            return sourceTicks;
        }
        if (!File.Exists(projection.Target)) return sourceTicks;

        if (oldImport.Contains(projection.Target))
        {
            plan.Operations.Add(new DeploymentPlan.RemoveBuildFile(projection.Target));
            handledRetired.Add(projection.Target);
            return sourceTicks;
        }
        if (!oldPersist.TryGetValue(projection.Target, out var previous)) throw Conflict(projection.Target);
        if (sourceTicks != previous.LastWriteTimeUtcTicks)
        {
            plan.Operations.Add(new DeploymentPlan.RemoveBuildFile(projection.Target));
            return sourceTicks;
        }

        var projectedTicks = File.GetLastWriteTimeUtc(projection.Target).Ticks;
        plan.Operations.Add(new DeploymentPlan.BackportPersistFile(projection.Target, projection.Source, sourceTicks));
        return projectedTicks;
    }

    private static void PlanPersistDirectory(
        DeploymentTarget.Projection projection,
        string build,
        ISet<string> oldImport,
        IReadOnlyDictionary<string, PersistProjectionManifest.FileEntry> oldPersist,
        ISet<string> handledRetired,
        DeploymentPlan plan,
        CancellationToken token)
    {
        if (DeploymentFileHelper.HasLinkAtOrAbove(projection.Target, build)) return;
        if (File.Exists(projection.Target))
        {
            if (oldImport.Contains(projection.Target))
            {
                plan.Operations.Add(new DeploymentPlan.RemoveBuildFile(projection.Target));
                handledRetired.Add(projection.Target);
                return;
            }
            if (oldPersist.ContainsKey(projection.Target))
            {
                plan.Operations.Add(new DeploymentPlan.RemoveBuildFile(projection.Target));
                return;
            }
            throw Conflict(projection.Target);
        }
        if (!Directory.Exists(projection.Target)) return;

        var pending = new Stack<(string Source, string Target)>();
        pending.Push((projection.Target, projection.Source));
        while (pending.TryPop(out var current))
        {
            token.ThrowIfCancellationRequested();
            if (File.Exists(current.Target))
            {
                if (!DirectoryCanBeReplaced(current.Source, oldImport)) throw Conflict(current.Source);
                PlanTrackedImportDirectoryRemoval(current.Source, oldImport, handledRetired, plan);
                continue;
            }
            foreach (var entry in new DirectoryInfo(current.Source).EnumerateFileSystemInfos())
            {
                if (entry.LinkTarget is not null) continue;
                var target = Path.Combine(current.Target, entry.Name);
                if (entry is DirectoryInfo)
                {
                    pending.Push((entry.FullName, target));
                    continue;
                }
                if (Directory.Exists(target))
                {
                    if (oldImport.Contains(entry.FullName))
                    {
                        plan.Operations.Add(new DeploymentPlan.RemoveBuildFile(entry.FullName));
                        handledRetired.Add(entry.FullName);
                        continue;
                    }
                    if (oldPersist.ContainsKey(entry.FullName))
                    {
                        plan.Operations.Add(new DeploymentPlan.RemoveBuildFile(entry.FullName));
                        continue;
                    }
                    throw Conflict(entry.FullName);
                }
                if (File.Exists(target))
                {
                    if (oldImport.Contains(entry.FullName))
                    {
                        plan.Operations.Add(new DeploymentPlan.RemoveBuildFile(entry.FullName));
                        handledRetired.Add(entry.FullName);
                        continue;
                    }
                    if (!oldPersist.TryGetValue(entry.FullName, out var previous)) throw Conflict(entry.FullName);
                    var sourceTicks = File.GetLastWriteTimeUtc(target).Ticks;
                    if (sourceTicks != previous.LastWriteTimeUtcTicks)
                        plan.Operations.Add(new DeploymentPlan.RemoveBuildFile(entry.FullName));
                    else
                        plan.Operations.Add(new DeploymentPlan.BackportPersistFile(entry.FullName, target, sourceTicks));
                }
                else
                {
                    plan.Operations.Add(new DeploymentPlan.MoveToPersist(entry.FullName, target));
                    if (oldImport.Contains(entry.FullName)) handledRetired.Add(entry.FullName);
                }
            }
        }
    }

    private static void PlanTrackedImportDirectoryRemoval(
        string directory,
        ISet<string> oldImport,
        ISet<string> handledRetired,
        DeploymentPlan plan)
    {
        foreach (var file in oldImport.Where(x => FileHelper.IsInDirectory(x, directory)).OrderByDescending(x => x.Length))
        {
            plan.Operations.Add(new DeploymentPlan.RemoveBuildFile(file));
            handledRetired.Add(file);
        }
    }

    private static bool DirectoryCanBeReplaced(string directory, ISet<string> oldImport) =>
        DeploymentFileHelper.DirectoryContainsOnlyLinksEmptyDirectoriesOrFiles(directory, oldImport);

    private static void AppendLinkOperations(
        string build,
        IEnumerable<DeploymentTarget.Projection> projections,
        ICollection<DeploymentPlan.Operation> operations,
        CancellationToken token)
    {
        if (DeploymentFileHelper.LinkTarget(build) is not null)
            throw new InvalidDataException($"The run directory cannot be a symbolic link: {build}");
        var remaining = projections.ToDictionary(x => x.Target, FileHelper.PathComparer);
        foreach (var current in DeploymentFileHelper.EnumerateLinks(build, token).OrderByDescending(x => x.Path.Length))
        {
            token.ThrowIfCancellationRequested();
            if (remaining.TryGetValue(current.Path, out var desired)
                && current.Directory == desired.Directory
                && DeploymentFileHelper.LinkMatches(current.Path, desired.Source))
            {
                remaining.Remove(current.Path);
                continue;
            }
            operations.Add(new DeploymentPlan.RemoveLink(current.Path));
        }

        foreach (var desired in remaining.Values.OrderBy(x => x.Target.Length).ThenBy(x => x.Target, FileHelper.PathComparer))
        {
            token.ThrowIfCancellationRequested();
            operations.Add(new DeploymentPlan.EnsureLink(desired.Target, desired.Source, desired.Directory));
        }
    }

    private static bool PersistManifestEquals(PersistProjectionManifest left, PersistProjectionManifest right)
    {
        if (left.Files.Count != right.Files.Count) return false;
        return left.Files.Zip(right.Files).All(x => FileHelper.PathComparer.Equals(x.First.Path, x.Second.Path)
            && x.First.LastWriteTimeUtcTicks == x.Second.LastWriteTimeUtcTicks);
    }

    private static BuildArtifactConflictException Conflict(string path) =>
        new(path, BuildArtifactConflictException.ConflictKind.OccupiedByRegularFileSystemEntry);
}
