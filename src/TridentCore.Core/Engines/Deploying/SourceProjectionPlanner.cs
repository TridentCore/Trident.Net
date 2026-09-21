using TridentCore.Abstractions;
using TridentCore.Abstractions.Utilities;
using TridentCore.Core.Utilities;

namespace TridentCore.Core.Engines.Deploying;

public sealed class SourceProjectionPlanner(ProjectionArbitrator arbitrator)
{
    public IReadOnlyList<DeploymentTarget.Projection> CreateTarget(string key, CancellationToken token = default)
    {
        var build = PathDef.Default.DirectoryOfBuild(key);
        var candidates = new Dictionary<string, DeploymentTarget.Projection>(FileHelper.PathComparer);
        Collect(PathDef.Default.DirectoryOfImport(key), build, DeploymentTarget.ProjectionKind.Import, candidates, token);
        Collect(PathDef.Default.DirectoryOfPersist(key), build, DeploymentTarget.ProjectionKind.Persist, candidates, token);
        return arbitrator.Select(candidates.Values);
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
}
