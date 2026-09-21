using TridentCore.Abstractions;
using TridentCore.Abstractions.Utilities;
using TridentCore.Core.Exceptions;
using TridentCore.Core.Utilities;

namespace TridentCore.Core.Engines.Deploying;

public sealed class DeploymentDiffer
{
    public DeploymentPlan Diff(string key, DeploymentTarget target, CancellationToken token = default)
    {
        var session = new Session(key, target);
        session.EvaluateRequirements(token);

        foreach (var projection in target.Projections.Where(x => x.Kind == DeploymentTarget.ProjectionKind.Package))
        {
            token.ThrowIfCancellationRequested();
            session.PlanPackage(projection);
        }

        foreach (var projection in target.Projections.Where(x => x.Kind == DeploymentTarget.ProjectionKind.Persist && x.Directory))
        {
            token.ThrowIfCancellationRequested();
            session.PlanPersistDirectory(projection, token);
        }

        foreach (var projection in target.Projections.Where(x => x.Kind == DeploymentTarget.ProjectionKind.Persist && !x.Directory))
        {
            token.ThrowIfCancellationRequested();
            session.PlanPersistFile(projection);
        }

        foreach (var retired in session.OldImportPaths.Where(x => !session.NewImportPaths.Contains(x) && !session.HandledRetired.Contains(x))
                     .OrderByDescending(x => x.Length))
        {
            token.ThrowIfCancellationRequested();
            session.PlanRetiredImport(retired);
        }

        foreach (var projection in target.Projections.Where(x => x.Kind == DeploymentTarget.ProjectionKind.Import)
                     .OrderBy(x => x.Target, FileHelper.PathComparer))
        {
            token.ThrowIfCancellationRequested();
            session.PlanImportFile(projection);
        }

        session.FinalizePlan(token);
        return session.Plan;
    }

    // NOTE: Operations 不是无序集合——retired import 的清理必须先于 import/persist 的物化，
    //  否则文件/目录形态转换（foo/bar → foo）会被残留的旧内容卡成冲突；链接对账必须最后，
    //  在真实内容就位后收敛投影。阶段内保持无序。
    private sealed class Session
    {
        public Session(string key, DeploymentTarget target)
        {
            Key = key;
            Target = target;
            Build = PathDef.Default.DirectoryOfBuild(key);
            OldImportManifest = ProjectionManifestHelper.ReadImport(key);
            OldImportPaths = OldImportManifest.Files
                .Select(x => ProjectionManifestHelper.ResolveStoredPath(Build, x))
                .ToHashSet(FileHelper.PathComparer);
            OldPersistManifest = ProjectionManifestHelper.ReadPersist(key);
            OldPersist = OldPersistManifest.Files.ToDictionary(
                x => ProjectionManifestHelper.ResolveStoredPath(Build, x.Path), FileHelper.PathComparer);
            NewImportPaths = target.Projections
                .Where(x => x.Kind == DeploymentTarget.ProjectionKind.Import)
                .Select(x => x.Target)
                .ToHashSet(FileHelper.PathComparer);
        }

        private string Key { get; }
        private DeploymentTarget Target { get; }
        private string Build { get; }
        private ImportProjectionManifest OldImportManifest { get; }
        private PersistProjectionManifest OldPersistManifest { get; }
        private IReadOnlyDictionary<string, PersistProjectionManifest.FileEntry> OldPersist { get; }
        public DeploymentPlan Plan { get; } = new();
        public ISet<string> OldImportPaths { get; }
        public ISet<string> NewImportPaths { get; }
        public HashSet<string> HandledRetired { get; } = new(FileHelper.PathComparer);
        public List<PersistProjectionManifest.FileEntry> PersistEntries { get; } = [];

        public void EvaluateRequirements(CancellationToken token)
        {
            foreach (var requirement in Target.Requirements)
            {
                token.ThrowIfCancellationRequested();
                if (FileHelper.VerifyModified(requirement.Path, null, requirement.Hash))
                {
                    if (requirement.Executable && !OperatingSystem.IsWindows()
                        && (File.GetUnixFileMode(requirement.Path) & UnixFileMode.UserExecute) == 0)
                        Plan.Operations.Add(new DeploymentPlan.MakeExecutable(requirement.Path));
                }
                else
                {
                    if (requirement.Url is null)
                        throw new InvalidDataException($"Local deployment source is missing or changed: {requirement.Path}");
                    Plan.Downloads.Add(new(
                        requirement.Path, requirement.Url, requirement.Hash, requirement.Executable));
                }
            }
        }

        public void PlanPackage(DeploymentTarget.Projection projection)
        {
            if (DeploymentFileHelper.HasLinkAtOrAbove(projection.Target, Build)) return;
            if (File.Exists(projection.Target) && !OldImportPaths.Contains(projection.Target))
                throw BuildArtifactConflictException.Occupied(projection.Target);
            if (Directory.Exists(projection.Target) && !DirectoryCanBeReplaced(projection.Target))
                throw BuildArtifactConflictException.Occupied(projection.Target);
        }

        public void PlanImportFile(DeploymentTarget.Projection projection)
        {
            if (DeploymentFileHelper.HasLinkAtOrAbove(projection.Target, Build))
            {
                Plan.Operations.Add(new DeploymentPlan.EnsureImportFile(projection.Source, projection.Target));
                return;
            }
            if (Directory.Exists(projection.Target) && !DirectoryCanBeReplaced(projection.Target))
                throw BuildArtifactConflictException.Occupied(projection.Target);
            if (!File.Exists(projection.Target))
                Plan.Operations.Add(new DeploymentPlan.EnsureImportFile(projection.Source, projection.Target));
        }

        public void PlanRetiredImport(string path)
        {
            if (!DeploymentFileHelper.HasLinkAtOrAbove(path, Build) && File.Exists(path))
                Plan.Operations.Add(new DeploymentPlan.RemoveBuildFile(path));
        }

        public void PlanPersistFile(DeploymentTarget.Projection projection)
        {
            var persistTicks = File.GetLastWriteTimeUtc(projection.Source).Ticks;
            var ticks = persistTicks;
            if (!DeploymentFileHelper.HasLinkAtOrAbove(projection.Target, Build))
            {
                if (Directory.Exists(projection.Target))
                {
                    if (!DirectoryCanBeReplaced(projection.Target))
                        throw BuildArtifactConflictException.Occupied(projection.Target);
                }
                else if (File.Exists(projection.Target))
                {
                    ticks = ArbitratePersistPair(projection.Target, projection.Source, persistTicks);
                }
            }
            PersistEntries.Add(new(ProjectionManifestHelper.ToStoredPath(Build, projection.Target), ticks));
        }

        public void PlanPersistDirectory(DeploymentTarget.Projection projection, CancellationToken token)
        {
            if (DeploymentFileHelper.HasLinkAtOrAbove(projection.Target, Build)) return;
            if (File.Exists(projection.Target))
            {
                if (OldImportPaths.Contains(projection.Target))
                {
                    Plan.Operations.Add(new DeploymentPlan.RemoveBuildFile(projection.Target));
                    HandledRetired.Add(projection.Target);
                    return;
                }
                if (OldPersist.ContainsKey(projection.Target))
                {
                    Plan.Operations.Add(new DeploymentPlan.RemoveBuildFile(projection.Target));
                    return;
                }
                throw BuildArtifactConflictException.Occupied(projection.Target);
            }
            if (!Directory.Exists(projection.Target)) return;

            var pending = new Stack<(string Source, string Target)>();
            pending.Push((projection.Target, projection.Source));
            while (pending.TryPop(out var current))
            {
                token.ThrowIfCancellationRequested();
                if (File.Exists(current.Target))
                {
                    if (!DirectoryCanBeReplaced(current.Source))
                        throw BuildArtifactConflictException.Occupied(current.Source);
                    PlanTrackedImportDirectoryRemoval(current.Source);
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
                        if (OldImportPaths.Contains(entry.FullName))
                        {
                            Plan.Operations.Add(new DeploymentPlan.RemoveBuildFile(entry.FullName));
                            HandledRetired.Add(entry.FullName);
                            continue;
                        }
                        if (OldPersist.ContainsKey(entry.FullName))
                        {
                            Plan.Operations.Add(new DeploymentPlan.RemoveBuildFile(entry.FullName));
                            continue;
                        }
                        throw BuildArtifactConflictException.Occupied(entry.FullName);
                    }
                    if (File.Exists(target))
                        ArbitratePersistPair(entry.FullName, target, File.GetLastWriteTimeUtc(target).Ticks);
                    else
                    {
                        Plan.Operations.Add(new DeploymentPlan.MoveToPersist(entry.FullName, target));
                        if (OldImportPaths.Contains(entry.FullName)) HandledRetired.Add(entry.FullName);
                    }
                }
            }
        }

        // 单文件 persist 投影与目录投影内的条目共用同一套 Delete-Create 仲裁：
        //  build 侧是受管 import 则退休；无归属证据则冲突；persist 源自上次部署后变过则丢弃 build 副本；
        //  否则把 build 副本搬回 persist（玩家修改胜出）并以其 mtime 作为新基线。
        private long ArbitratePersistPair(string buildPath, string persistPath, long persistTicks)
        {
            if (OldImportPaths.Contains(buildPath))
            {
                Plan.Operations.Add(new DeploymentPlan.RemoveBuildFile(buildPath));
                HandledRetired.Add(buildPath);
                return persistTicks;
            }
            if (!OldPersist.TryGetValue(buildPath, out var previous))
                throw BuildArtifactConflictException.Occupied(buildPath);
            // WARNING: persist mtime 变化不能证明它比 build 更新。程序可能先通过链接 Open-Overwrite persist，
            //  再以 Delete-Create 写出更晚的 build 普通文件；当前策略仍让 persist 胜出并删除该 build 副本。
            //  在双副本仲裁方案闭合前，不要把这里的 mtime 判断视为完整的写入先后证据。
            if (persistTicks != previous.LastWriteTimeUtcTicks)
            {
                Plan.Operations.Add(new DeploymentPlan.RemoveBuildFile(buildPath));
                return persistTicks;
            }
            var buildTicks = File.GetLastWriteTimeUtc(buildPath).Ticks;
            Plan.Operations.Add(new DeploymentPlan.BackportPersistFile(buildPath, persistPath, persistTicks));
            return buildTicks;
        }

        public void FinalizePlan(CancellationToken token)
        {
            Plan.ImportManifest = new()
            {
                Files = [.. NewImportPaths.Select(x => ProjectionManifestHelper.ToStoredPath(Build, x))
                    .OrderBy(x => x, FileHelper.PathComparer)]
            };
            Plan.PersistManifest = new()
            {
                Files = [.. PersistEntries.OrderBy(x => x.Path, FileHelper.PathComparer)]
            };
            AppendLinkOperations(token);
            Plan.NeedsManifestCommit = !File.Exists(PathDef.Default.FileOfImportProjectionManifest(Key))
                || !File.Exists(PathDef.Default.FileOfPersistProjectionManifest(Key))
                || !OldImportManifest.Files.SequenceEqual(Plan.ImportManifest.Files, FileHelper.PathComparer)
                || !PersistManifestEquals(OldPersistManifest, Plan.PersistManifest);
        }

        private void PlanTrackedImportDirectoryRemoval(string directory)
        {
            foreach (var file in OldImportPaths.Where(x => FileHelper.IsInDirectory(x, directory)).OrderByDescending(x => x.Length))
            {
                Plan.Operations.Add(new DeploymentPlan.RemoveBuildFile(file));
                HandledRetired.Add(file);
            }
        }

        private bool DirectoryCanBeReplaced(string directory) =>
            DeploymentFileHelper.DirectoryContainsOnlyLinksEmptyDirectoriesOrFiles(directory, OldImportPaths);

        private void AppendLinkOperations(CancellationToken token)
        {
            if (DeploymentFileHelper.LinkTarget(Build) is not null)
                throw new InvalidDataException($"The run directory cannot be a symbolic link: {Build}");
            var remaining = Target.Projections.Where(x => x.Kind != DeploymentTarget.ProjectionKind.Import)
                .ToDictionary(x => x.Target, FileHelper.PathComparer);
            foreach (var current in DeploymentFileHelper.EnumerateLinks(Build, token).OrderByDescending(x => x.Path.Length))
            {
                token.ThrowIfCancellationRequested();
                if (remaining.TryGetValue(current.Path, out var desired)
                    && current.Directory == desired.Directory
                    && DeploymentFileHelper.LinkMatches(current.Path, desired.Source))
                {
                    remaining.Remove(current.Path);
                    continue;
                }
                Plan.Operations.Add(new DeploymentPlan.RemoveLink(current.Path));
            }

            foreach (var desired in remaining.Values.OrderBy(x => x.Target.Length).ThenBy(x => x.Target, FileHelper.PathComparer))
            {
                token.ThrowIfCancellationRequested();
                Plan.Operations.Add(new DeploymentPlan.EnsureLink(desired.Target, desired.Source, desired.Directory));
            }
        }

        private static bool PersistManifestEquals(PersistProjectionManifest left, PersistProjectionManifest right)
        {
            if (left.Files.Count != right.Files.Count) return false;
            return left.Files.Zip(right.Files).All(x => FileHelper.PathComparer.Equals(x.First.Path, x.Second.Path)
                && x.First.LastWriteTimeUtcTicks == x.Second.LastWriteTimeUtcTicks);
        }
    }
}
