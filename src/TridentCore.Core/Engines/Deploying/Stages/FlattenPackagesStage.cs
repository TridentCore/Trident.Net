using TridentCore.Abstractions.FileModels;
using TridentCore.Abstractions.Utilities;
using TridentCore.Core.Exceptions;
using TridentCore.Core.Extensions;

namespace TridentCore.Core.Engines.Deploying.Stages;

// NOTE: 对 Lock.Packages 的两遍叠加仲裁，在 SyncPackages 让每个 (project, source) 存活后执行。
//  两遍共用同一例程——找重复键、按 source 优先级选唯一赢家、抑制其余；同层平局不可解，抛
//  PackageConflictException。
//
//  1. Project 遍（键 = label/ns/pid）：同一项目来自多个 source 时按优先级裁决，赢家物化，
//     输家保持锁定（SuppressedBy 指向赢家），版本在将来重排后仍在而不重解析。
//  2. Path 遍（键 = build 内 RelativeTarget）：不同项目落到同一文件时同样裁决。
//
//  仲裁是内部的：产出稳定的 Lock.Packages（赢家生效、输家标记）。唯一逃逸的是
//  PackageConflictException——同层平局无法裁决时使部署失败，交由用户解决。
public class FlattenPackagesStage : StageBase
{
    protected override Task OnProcessAsync(CancellationToken token)
    {
        var setup = Context.Setup;

        var afterProject = Arbitrate(Context.Lock.Packages,
                                     ProjectKeyOf,
                                     (_, winner) => winner.Resolved.ProjectName,
                                     setup);

        var survivors = afterProject.Where(p => p.SuppressedBy is null);
        var afterPath = Arbitrate(survivors, p => p.RelativeTarget(), (target, _) => target, setup);

        var result = afterProject.Where(p => p.SuppressedBy is not null).Concat(afterPath).ToList();

        Context.Lock = Context.Lock with { Packages = result };
        return Task.CompletedTask;
    }

    // NOTE: 按键去重——单成员直通；多个按叠加优先级排序取顶（物化）、其余抑制；同层平局不可解。
    private static List<LockData.LockedPackage> Arbitrate(
        IEnumerable<LockData.LockedPackage> items,
        Func<LockData.LockedPackage, string> keyOf,
        Func<string, LockData.LockedPackage, string> subjectOf,
        Profile.Rice setup)
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

            var ranked = members.Select(p => (Pkg: p, Rank: RankOf(p, setup))).OrderByDescending(x => x.Rank).ToList();

            var topRank = ranked[0].Rank;
            if (ranked.Count(x => x.Rank.CompareTo(topRank) == 0) > 1)
            {
                throw new PackageConflictException(subjectOf(group.Key, ranked[0].Pkg),
                [
                    .. ranked.Where(x => x.Rank.CompareTo(topRank) == 0).Select(x => x.Pkg)
                ]);
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

    // NOTE: (Tier, Index)：手动 3 > SourceOrders 列出的 2（末位最高）> 未列出的非整合包 1 >
    //  当前整合包（Setup.Source）0。列进 SourceOrders 即声明显式叠加层。
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
            return string.Concat(parsed.Repository.ToLowerInvariant(),
                                 "|",
                                 parsed.Namespace ?? string.Empty,
                                 "|",
                                 parsed.Identity);
        }

        throw new FormatException("Invalid pref: " + p.Pref);
    }
}
