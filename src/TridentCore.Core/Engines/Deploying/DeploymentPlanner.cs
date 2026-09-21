using TridentCore.Abstractions;
using TridentCore.Abstractions.FileModels;
using TridentCore.Abstractions.Utilities;
using TridentCore.Core.Exceptions;
using TridentCore.Core.Extensions;
using TridentCore.Core.Utilities;

namespace TridentCore.Core.Engines.Deploying;

public sealed class DeploymentPlanner(PackagePlanner packages)
{
    public DeploymentTarget CreateTarget(string key, LockData data, CancellationToken token = default)
    {
        var target = new DeploymentTarget();
        var artifact = data.Artifact ?? throw new InvalidDataException("Lock has no launch artifact.");
        foreach (var group in artifact.AllLibraries().Concat(artifact.Agents.Select(x => x.Library))
            .GroupBy(x => x.FilePath(key), FileHelper.PathComparer))
        {
            token.ThrowIfCancellationRequested();
            var library = group.FirstOrDefault(x => x.Hash is not null) ?? group.First();
            DeploymentFileHelper.RequireFile(target, group.Key, library.LocalPath is null ? library.Url : null, library.Hash);
        }

        var build = PathDef.Default.DirectoryOfBuild(key);
        var candidates = new Dictionary<string, DeploymentTarget.Projection>(FileHelper.PathComparer);
        foreach (var package in packages.Plan(data))
        {
            token.ThrowIfCancellationRequested();
            var parsed = PackageHelper.Parse(package.Pref);
            var source = PathDef.Default.FileOfPackageObject(parsed.Repository, parsed.Namespace, parsed.Identity,
                package.Resolved.VersionId, Path.GetExtension(package.Resolved.FileName));
            var projection = new DeploymentTarget.Projection(
                source,
                DeploymentFileHelper.ProjectionPath(build, package.RelativeTarget()),
                DeploymentTarget.ProjectionKind.Package,
                false,
                package.Resolved.Download,
                package.Resolved.Hash);
            DeploymentFileHelper.RequireProjection(candidates, build, projection);
        }
        Collect(PathDef.Default.DirectoryOfImport(key), build, DeploymentTarget.ProjectionKind.Import, candidates, token);
        Collect(PathDef.Default.DirectoryOfPersist(key), build, DeploymentTarget.ProjectionKind.Persist, candidates, token);

        target.Projections.AddRange(Select(candidates.Values));
        foreach (var projection in target.Projections.Where(x => x.Kind == DeploymentTarget.ProjectionKind.Package))
            DeploymentFileHelper.RequireFile(target, projection.Source, projection.Url, projection.Hash);
        return target;
    }

    private static List<DeploymentTarget.Projection> Select(IEnumerable<DeploymentTarget.Projection> candidates)
    {
        var selected = new List<DeploymentTarget.Projection>();
        foreach (var candidate in candidates.OrderByDescending(x => x.Kind).ThenBy(x => x.Target.Length))
        {
            var ancestor = selected.FirstOrDefault(x => FileHelper.IsInDirectory(candidate.Target, x.Target));
            if (ancestor is not null)
            {
                if (ancestor.Kind == candidate.Kind) throw Conflict(candidate.Target);
                continue;
            }

            var descendants = selected.Where(x => FileHelper.IsInDirectory(x.Target, candidate.Target)).ToArray();
            if (descendants.Length != 0)
            {
                if (descendants.Any(x => x.Kind == candidate.Kind)) throw Conflict(candidate.Target);
                continue;
            }
            selected.Add(candidate);
        }
        return selected;
    }

    private static void Collect(
        string source,
        string build,
        DeploymentTarget.ProjectionKind kind,
        IDictionary<string, DeploymentTarget.Projection> candidates,
        CancellationToken token)
    {
        if (DeploymentFileHelper.LinkTarget(source) is not null)
            throw new InvalidDataException($"Managed source directory cannot be a symbolic link: {source}");
        if (!Directory.Exists(source)) return;
        var pending = new Stack<(string Directory, bool Covered)>();
        pending.Push((source, false));
        while (pending.TryPop(out var current))
        {
            token.ThrowIfCancellationRequested();
            var (directory, covered) = current;
            if (kind == DeploymentTarget.ProjectionKind.Persist && File.Exists(Path.Combine(directory, ".keep")))
            {
                if (FileHelper.IsPathEquivalent(source, directory))
                    throw new InvalidDataException("The persistence root cannot be projected as one directory.");
                if (!covered)
                {
                    var target = DeploymentFileHelper.ProjectionPath(build, Path.GetRelativePath(source, directory));
                    DeploymentFileHelper.RequireProjection(candidates, build, new(directory, target, kind, true, null, null));
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
                    var target = DeploymentFileHelper.ProjectionPath(build, Path.GetRelativePath(source, entry.FullName));
                    DeploymentFileHelper.RequireProjection(candidates, build, new(entry.FullName, target, kind, false, null, null));
                }
            }
        }
    }

    private static BuildArtifactConflictException Conflict(string path) =>
        new(path, BuildArtifactConflictException.ConflictKind.OccupiedByRegularFileSystemEntry);
}
