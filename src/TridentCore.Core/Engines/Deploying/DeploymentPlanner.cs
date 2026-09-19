using TridentCore.Abstractions;
using TridentCore.Abstractions.FileModels;
using TridentCore.Abstractions.Utilities;
using TridentCore.Core.Exceptions;
using TridentCore.Core.Extensions;
using TridentCore.Core.Utilities;

namespace TridentCore.Core.Engines.Deploying;

public sealed class DeploymentPlanner(PackagePlanner packages)
{
    public DeploymentPlan Plan(string key, LockData data, CancellationToken token = default)
    {
        var plan = new DeploymentPlan();
        var artifact = data.Artifact ?? throw new InvalidDataException("Lock has no launch artifact.");
        foreach (var group in artifact.AllLibraries().Concat(artifact.Agents.Select(x => x.Library))
            .GroupBy(x => x.FilePath(key), FileHelper.PathComparer))
        {
            token.ThrowIfCancellationRequested();
            var library = group.FirstOrDefault(x => x.Hash is not null) ?? group.First();
            FilePlanningHelper.RequireFile(plan, group.Key, library.LocalPath is null ? library.Url : null, library.Hash);
        }

        var build = PathDef.Default.DirectoryOfBuild(key);
        var candidates = new Dictionary<string, Projection>(FileHelper.PathComparer);
        var packageLinks = new Dictionary<string, string>(FileHelper.PathComparer);
        foreach (var package in packages.Plan(data))
        {
            var parsed = PackageHelper.Parse(package.Pref);
            var source = PathDef.Default.FileOfPackageObject(parsed.Repository, parsed.Namespace, parsed.Identity,
                package.Resolved.VersionId, Path.GetExtension(package.Resolved.FileName));
            var target = FilePlanningHelper.ProjectionPath(build, package.RelativeTarget());
            candidates[target] = new(source, target, 0, false, package.Resolved.Download, package.Resolved.Hash);
            packageLinks[target] = source;
        }
        Collect(PathDef.Default.DirectoryOfImport(key), build, 1, candidates, token);
        Collect(PathDef.Default.DirectoryOfPersist(key), build, 2, candidates, token);

        var selected = new List<Projection>();
        foreach (var candidate in candidates.Values.OrderByDescending(x => x.Priority).ThenBy(x => x.Target.Length))
        {
            if (selected.Any(x => x.Directory && FileHelper.IsInDirectory(candidate.Target, x.Target))) continue;
            selected.Add(candidate);
        }
        foreach (var projection in selected.Where(x => !x.Directory))
        {
            if (selected.Any(x => !FileHelper.IsPathEquivalent(x.Target, projection.Target)
                && FileHelper.IsInDirectory(x.Target, projection.Target)))
                throw Conflict(projection.Target);
        }
        var links = selected.Where(x => x.Priority != 1).ToDictionary(x => x.Target, FileHelper.PathComparer);
        var existingLinks = EnumerateLinks(build, token).ToDictionary(x => x.Path, FileHelper.PathComparer);
        foreach (var existing in existingLinks.Values.OrderByDescending(x => x.Path.Length))
        {
            if (!links.TryGetValue(existing.Path, out var desired) || existing.Directory != desired.Directory
                || !FilePlanningHelper.LinkMatches(existing.Path, desired.Source))
                plan.Operations.Add(existing);
        }

        foreach (var projection in selected.OrderBy(x => x.Target, FileHelper.PathComparer))
        {
            token.ThrowIfCancellationRequested();
            var removedParent = plan.Operations.OfType<DeploymentPlan.RemoveLink>().Any(x => x.Directory
                && !FileHelper.IsPathEquivalent(x.Path, projection.Target)
                && FileHelper.IsInDirectory(projection.Target, x.Path));
            if (projection.Priority == 0)
                FilePlanningHelper.RequireFile(plan, projection.Source, projection.Url, projection.Hash);
            if (projection.Priority == 1)
            {
                if (existingLinks.ContainsKey(projection.Target))
                    throw new BuildArtifactConflictException(projection.Target, BuildArtifactConflictException.ConflictKind.LegacyImportProjection);
                if (!removedParent && Directory.Exists(projection.Target)) throw Conflict(projection.Target);
                if (removedParent || !File.Exists(projection.Target)) plan.Operations.Add(new DeploymentPlan.Copy(projection.Source, projection.Target));
                continue;
            }
            if (existingLinks.TryGetValue(projection.Target, out var existingLink))
            {
                if (existingLink.Directory != projection.Directory || !FilePlanningHelper.LinkMatches(projection.Target, projection.Source))
                    plan.Operations.Add(new DeploymentPlan.Link(projection.Target, projection.Source, projection.Directory));
                continue;
            }
            if (!removedParent && projection.Priority == 2)
            {
                if (projection.Directory)
                {
                    if (File.Exists(projection.Target)) throw Conflict(projection.Target);
                    if (Directory.Exists(projection.Target))
                    {
                        foreach (var existing in existingLinks.Values.Where(x => FileHelper.IsInDirectory(x.Path, projection.Target)))
                        {
                            var relative = Path.GetRelativePath(projection.Target, existing.Path);
                            var persisted = Path.Combine(projection.Source, relative);
                            if ((packageLinks.TryGetValue(existing.Path, out var packageSource)
                                 && FilePlanningHelper.LinkMatches(existing.Path, packageSource))
                                || (Path.Exists(persisted) && FilePlanningHelper.LinkMatches(existing.Path, persisted)))
                                continue;
                            throw new InvalidDataException($"Cannot backport an unmanaged symbolic link: {existing.Path}");
                        }
                        BackportDirectory(projection.Target, projection.Source, plan, token);
                    }
                }
                else
                {
                    if (Directory.Exists(projection.Target)) throw Conflict(projection.Target);
                    if (File.Exists(projection.Target)) plan.Operations.Add(new DeploymentPlan.Move(projection.Target, projection.Source));
                }
            }
            else if (!removedParent && Path.Exists(projection.Target)) throw Conflict(projection.Target);
            plan.Operations.Add(new DeploymentPlan.Link(projection.Target, projection.Source, projection.Directory));
        }
        if (!Directory.Exists(build)) plan.Operations.Insert(0, new DeploymentPlan.CreateDirectory(build));
        return plan;
    }

    private static void Collect(string source, string build, int priority, IDictionary<string, Projection> candidates, CancellationToken token)
    {
        if (FilePlanningHelper.LinkTarget(source) is not null)
            throw new InvalidDataException($"Managed source directory cannot be a symbolic link: {source}");
        if (!Directory.Exists(source)) return;
        var pending = new Stack<(string Directory, bool Covered)>();
        pending.Push((source, false));
        while (pending.TryPop(out var current))
        {
            token.ThrowIfCancellationRequested();
            var (directory, covered) = current;
            if (priority == 2 && File.Exists(Path.Combine(directory, ".keep")))
            {
                if (FileHelper.IsPathEquivalent(source, directory))
                    throw new InvalidDataException("The persistence root cannot be projected as one directory.");
                if (!covered)
                {
                    var target = FilePlanningHelper.ProjectionPath(build, Path.GetRelativePath(source, directory));
                    candidates[target] = new(directory, target, priority, true, null, null);
                    covered = true;
                }
            }
            foreach (var entry in new DirectoryInfo(directory).EnumerateFileSystemInfos())
            {
                if (entry.LinkTarget is not null)
                    throw new InvalidDataException($"Managed source cannot contain a symbolic link: {entry.FullName}");
                if (entry is DirectoryInfo)
                    pending.Push((entry.FullName, covered));
                else if (!covered)
                {
                    var target = FilePlanningHelper.ProjectionPath(build, Path.GetRelativePath(source, entry.FullName));
                    candidates[target] = new(entry.FullName, target, priority, false, null, null);
                }
            }
        }
    }

    private static IEnumerable<DeploymentPlan.RemoveLink> EnumerateLinks(string root, CancellationToken token)
    {
        if (!Directory.Exists(root)) yield break;
        if (FilePlanningHelper.LinkTarget(root) is not null) throw Conflict(root);
        var pending = new Stack<string>();
        pending.Push(root);
        while (pending.TryPop(out var directory))
        {
            token.ThrowIfCancellationRequested();
            foreach (var entry in new DirectoryInfo(directory).EnumerateFileSystemInfos())
            {
                if (entry.LinkTarget is not null)
                    yield return new(entry.FullName, (entry.Attributes & FileAttributes.Directory) != 0);
                else if (entry is DirectoryInfo) pending.Push(entry.FullName);
            }
        }
    }

    private static void BackportDirectory(string source, string target, DeploymentPlan plan, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        if (!Directory.Exists(target)) plan.Operations.Add(new DeploymentPlan.CreateDirectory(target));
        foreach (var entry in new DirectoryInfo(source).EnumerateFileSystemInfos())
        {
            if (entry.LinkTarget is not null)
            {
                if (!plan.Operations.OfType<DeploymentPlan.RemoveLink>().Any(x => FileHelper.IsPathEquivalent(x.Path, entry.FullName)))
                    throw new InvalidDataException($"Cannot backport an unmanaged symbolic link: {entry.FullName}");
                continue;
            }
            var destination = Path.Combine(target, entry.Name);
            if (entry is DirectoryInfo) BackportDirectory(entry.FullName, destination, plan, token);
            else plan.Operations.Add(new DeploymentPlan.Move(entry.FullName, destination));
        }
        plan.Operations.Add(new DeploymentPlan.RemoveDirectory(source));
    }

    private static BuildArtifactConflictException Conflict(string path) =>
        new(path, BuildArtifactConflictException.ConflictKind.OccupiedByRegularFileSystemEntry);

    private sealed record Projection(string Source, string Target, int Priority, bool Directory, Uri? Url, FileHash? Hash);
}
