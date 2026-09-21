using TridentCore.Core.Utilities;
using TridentCore.Core.Exceptions;

namespace TridentCore.Core.Engines.Deploying;

public sealed class ProjectionArbitrator
{
    public IReadOnlyList<DeploymentTarget.Projection> Select(IEnumerable<DeploymentTarget.Projection> candidates)
    {
        var selected = new List<DeploymentTarget.Projection>();
        foreach (var candidate in candidates.OrderByDescending(x => x.Kind).ThenBy(x => x.Target.Length))
        {
            var same = selected.FirstOrDefault(x => FileHelper.IsPathEquivalent(candidate.Target, x.Target));
            if (same is not null)
            {
                if (same.Kind == candidate.Kind) throw Conflict(candidate.Target);
                continue;
            }

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
