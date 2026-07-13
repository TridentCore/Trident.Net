using TridentCore.Abstractions.FileModels;
using TridentCore.Abstractions.Utilities;
using TridentCore.Core.Exceptions;
using TridentCore.Core.Extensions;

namespace TridentCore.Core.Engines.Deploying.Stages;

// Two-pass overlay arbitration over Lock.Packages, run after SyncPackages has let every
// (project, source) survive. Both passes share one routine — find duplicate keys, let source
// priority pick a single winner, suppress the rest; a same-tier tie is unresolvable and throws
// PackageConflictException.
//
//   1. Project pass (key = label/ns/pid) — same project from multiple sources resolves by source
//      priority. Winners are materialized; losers stay locked (SuppressedBy points at the winner)
//      so their version survives a future reshuffle without re-resolving.
//   2. Path pass (key = in-build RelativeTarget) — among project survivors, different projects
//      landing on the same file resolve the same way.
//
// Arbitration is internal: it produces a stable Lock.Packages (winners effective, losers marked).
// The only thing that escapes is PackageConflictException, thrown when a same-tier tie cannot be
// decided — that faults the deploy for the user to resolve.
public class FlattenPackagesStage : StageBase
{
    protected override Task OnProcessAsync(CancellationToken token)
    {
        var setup = Context.Setup;

        var afterProject = Arbitrate(
            Context.Lock.Packages,
            ProjectKeyOf,
            (_, winner) => winner.Resolved.ProjectName,
            setup);

        var survivors = afterProject.Where(p => p.SuppressedBy is null);
        var afterPath = Arbitrate(
            survivors,
            p => p.RelativeTarget(),
            (target, _) => target,
            setup);

        var result = afterProject
            .Where(p => p.SuppressedBy is not null)
            .Concat(afterPath)
            .ToList();

        Context.Lock = Context.Lock with { Packages = result };
        return Task.CompletedTask;
    }

    // Dedupe by key: a sole member passes through; multiple are ranked by overlay priority and
    // the top wins (materialized) while the rest are suppressed; a same-tier tie is unresolvable.
    private static List<LockData.LockedPackage> Arbitrate(
        IEnumerable<LockData.LockedPackage> items,
        Func<LockData.LockedPackage, string> keyOf,
        Func<string, LockData.LockedPackage, string> subjectOf,
        Profile.Rice setup
    )
    {
        var result = new List<LockData.LockedPackage>();

        foreach (var group in items.GroupBy(keyOf, StringComparer.OrdinalIgnoreCase))
        {
            var members = group.ToList();
            if (members.Count == 1)
            {
                result.Add(members[0] with { SuppressedBy = null });
                continue;
            }

            var ranked = members
                .Select(p => (Pkg: p, Rank: RankOf(p, setup)))
                .OrderByDescending(x => x.Rank)
                .ToList();

            var topRank = ranked[0].Rank;
            if (ranked.Count(x => x.Rank.CompareTo(topRank) == 0) > 1)
            {
                throw new PackageConflictException(
                    subjectOf(group.Key, ranked[0].Pkg),
                    ranked.Where(x => x.Rank.CompareTo(topRank) == 0).Select(x => x.Pkg).ToList());
            }

            var winner = ranked[0].Pkg;
            result.Add(winner with { SuppressedBy = null });
            foreach (var loser in ranked.Skip(1).Select(x => x.Pkg))
            {
                result.Add(loser with { SuppressedBy = winner.Pref });
            }
        }

        return result;
    }

    // (Tier, Index): manual 3 > listed-in-SourceOrders 2 (last is highest) > unlisted non-modpack
    // 1 > current modpack (Setup.Source) 0. Listing a source declares it an explicit overlay layer.
    private static (int Tier, int Index) RankOf(LockData.LockedPackage p, Profile.Rice setup)
    {
        if (p.Source == null)
        {
            return (3, 0);
        }

        var idx = setup.SourceOrders.IndexOf(p.Source);
        if (idx >= 0)
        {
            return (2, idx);
        }

        return p.Source == setup.Source ? (0, 0) : (1, 0);
    }

    private static string ProjectKeyOf(LockData.LockedPackage p)
    {
        if (PackageHelper.TryParse(p.Pref, out var parsed))
        {
            return string.Concat(
                (parsed.Label).ToLowerInvariant(),
                "|",
                parsed.Namespace ?? string.Empty,
                "|",
                parsed.Pid);
        }

        throw new FormatException("Invalid pref: " + p.Pref);
    }
}
