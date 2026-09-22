using System.Collections.Immutable;
using TridentCore.Abstractions;
using TridentCore.Abstractions.FileModels;
using TridentCore.Abstractions.Utilities;
using TridentCore.Core.Extensions;
using TridentCore.Core.Utilities;

namespace TridentCore.Core.Engines.Deploying;

public sealed class DeploymentPlanner(
    PackagePlanner packages,
    SourceProjectionPlanner sources,
    ProjectionArbitrator arbitrator)
{
    public DeploymentTarget CreateTarget(
        string key,
        LockData data,
        AssetIndex? assets,
        RuntimeIndex? runtime,
        CancellationToken token = default,
        Action<(int Current, int Total)>? progress = null)
    {
        var artifact = data.Artifact ?? throw new InvalidDataException("Lock has no launch artifact.");
        var libraries = artifact.AllLibraries().Concat(artifact.Agents.Select(x => x.Library))
            .GroupBy(x => x.FilePath(key), FileHelper.PathComparer).ToArray();
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
        var sourceProjections = sources.CreateTarget(key, token);
        var projections = arbitrator.Select(candidates.Values.Concat(sourceProjections), token).ToImmutableArray();
        var packageProjections = projections.Where(x => x.Kind == DeploymentTarget.ProjectionKind.Package).ToArray();
        var total = libraries.Length + packageProjections.Length + (assets?.Objects.Count ?? 0) + (runtime?.Files.Count ?? 0);
        var completed = 0;
        var requirements = new Dictionary<string, DeploymentFileRequirement>(FileHelper.PathComparer);
        if (total > 0) progress?.Invoke((0, total));

        foreach (var group in libraries)
        {
            var library = group.FirstOrDefault(x => x.Hash is not null) ?? group.First();
            Add(new(group.Key, library.LocalPath is null ? library.Url : null, library.Hash, false));
        }
        foreach (var projection in packageProjections)
            Add(new(projection.Source, projection.Url, projection.Hash, false));
        if (assets is not null)
            foreach (var requirement in new AssetPlanner().Plan(assets, token)) Add(requirement);
        if (runtime is not null)
            foreach (var requirement in new RuntimePlanner().Plan(runtime, token)) Add(requirement);

        return new() { Requirements = requirements.Values.ToImmutableArray(), Projections = projections };

        void Add(DeploymentFileRequirement requirement)
        {
            token.ThrowIfCancellationRequested();
            var path = Path.TrimEndingDirectorySeparator(Path.GetFullPath(requirement.Path));
            if (requirements.TryGetValue(path, out var existing))
            {
                if (existing.Hash != requirement.Hash || existing.Url != requirement.Url)
                    throw new InvalidDataException($"Conflicting file requirements: {requirement.Path}");
                if (requirement.Executable && !existing.Executable)
                    requirements[path] = existing with { Executable = true };
            }
            else
            {
                requirements.Add(path, requirement);
            }
            progress?.Invoke((++completed, total));
        }
    }
}
