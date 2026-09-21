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
        var sourceProjections = sources.CreateTarget(key, token);
        target.Projections.AddRange(arbitrator.Select(candidates.Values.Concat(sourceProjections)));
        foreach (var projection in target.Projections.Where(x => x.Kind == DeploymentTarget.ProjectionKind.Package))
            DeploymentFileHelper.RequireFile(target, projection.Source, projection.Url, projection.Hash);
        return target;
    }
}
