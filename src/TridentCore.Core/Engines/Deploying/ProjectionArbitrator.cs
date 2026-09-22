using TridentCore.Core.Exceptions;
using TridentCore.Core.Utilities;

namespace TridentCore.Core.Engines.Deploying;

public sealed class ProjectionArbitrator
{
    public IReadOnlyList<DeploymentTarget.Projection> Select(
        IEnumerable<DeploymentTarget.Projection> candidates, CancellationToken token = default)
    {
        var selected = new List<DeploymentTarget.Projection>();
        var paths = new Dictionary<string, DeploymentTarget.Projection>(FileHelper.PathComparer);
        var descendantKinds = new Dictionary<string, int>(FileHelper.PathComparer);
        foreach (var candidate in candidates.OrderByDescending(x => x.Kind).ThenBy(x => x.Target.Length))
        {
            token.ThrowIfCancellationRequested();
            var path = Path.TrimEndingDirectorySeparator(Path.GetFullPath(candidate.Target));
            if (paths.TryGetValue(path, out var same))
            {
                if (same.Kind == candidate.Kind) throw Conflict(candidate.Target);
                continue;
            }

            DeploymentTarget.Projection? ancestor = null;
            for (var parent = Path.GetDirectoryName(path); parent is not null; parent = Path.GetDirectoryName(parent))
            {
                if (paths.TryGetValue(parent, out ancestor)) break;
            }
            if (ancestor is not null)
            {
                if (ancestor.Kind == candidate.Kind) throw Conflict(candidate.Target);
                continue;
            }

            var kind = 1 << (int)candidate.Kind;
            if (descendantKinds.TryGetValue(path, out var descendants))
            {
                if ((descendants & kind) != 0) throw Conflict(candidate.Target);
                continue;
            }

            selected.Add(candidate);
            paths.Add(path, candidate);
            for (var parent = Path.GetDirectoryName(path); parent is not null; parent = Path.GetDirectoryName(parent))
                descendantKinds[parent] = descendantKinds.GetValueOrDefault(parent) | kind;
        }
        return selected;
    }

    public bool IsShadowed(
        DeploymentTarget.Projection candidate,
        IEnumerable<DeploymentTarget.Projection> selected) =>
        selected.Any(x => x.Kind > candidate.Kind && PathsOverlap(x.Target, candidate.Target));

    private static bool PathsOverlap(string left, string right) =>
        FileHelper.IsPathEquivalent(left, right)
        || FileHelper.IsInDirectory(left, right)
        || FileHelper.IsInDirectory(right, left);

    private static BuildArtifactConflictException Conflict(string path) =>
        new(path, BuildArtifactConflictException.ConflictKind.OccupiedByRegularFileSystemEntry);
}
