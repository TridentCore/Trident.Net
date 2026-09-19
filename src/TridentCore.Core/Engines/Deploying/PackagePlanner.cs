using Microsoft.Extensions.Logging;
using TridentCore.Abstractions.FileModels;
using TridentCore.Abstractions.Repositories.Resources;
using TridentCore.Abstractions.Utilities;
using TridentCore.Core.Exceptions;
using TridentCore.Core.Extensions;
using TridentCore.Core.Utilities;

namespace TridentCore.Core.Engines.Deploying;

public class PackagePlanner(ILogger<PackagePlanner> logger)
{
    public IReadOnlyList<PackagePlan> Plan(
        IReadOnlyList<(Profile.Rice.Entry Entry, Package Package)> resolved,
        IReadOnlyList<Profile.Rice.Rule> rules) =>
        [.. resolved.Select(x => ToPlan(x.Package, EvaluateRule(x.Entry, x.Package, rules)))];

    public LockData.PackageRule EvaluateRule(
        Profile.Rice.Entry entry,
        Package package,
        IReadOnlyList<Profile.Rice.Rule> rules)
    {
        var result = RuleHelper.Evaluate(new RuleHelper.Input(entry, package), rules);
        return ToPackageRule(result, entry);
    }

    private LockData.PackageRule ToPackageRule(RuleHelper.Result result, Profile.Rice.Entry entry)
    {
        if (result is { Matched: true, EffectiveRule: { } effectiveRule })
        {
            logger.LogDebug("Rule {{ {skipping}, {destination} }} applied to {pref}",
                            effectiveRule.Skipping,
                            effectiveRule.Destination ?? "<default>",
                            entry.Pref);

            return new(effectiveRule.Skipping, effectiveRule.Destination, effectiveRule.Normalizing);
        }

        return new(false, null, false);
    }

    public static PackagePlan ToPlan(Package package, LockData.PackageRule rule)
    {
        var relativeTarget = PackagePathHelper.RelativeTarget(rule.Normalizing,
                                                              rule.Destination,
                                                              package.ProjectName,
                                                              package.FileName,
                                                              package.Kind);

        return new(package.Label,
                   package.Namespace,
                   package.ProjectId,
                   package.VersionId,
                   relativeTarget,
                   package.Download,
                   package.Hash)
        { IsSkipping = rule.Skipping };
    }

    public IReadOnlyList<LockData.LockedPackage> Plan(LockData data)
    {
        var projects = Arbitrate(data.Packages, ProjectKeyOf,
            (_, winner) => winner.Resolved.ProjectName, data);
        return Arbitrate(projects, x => x.RelativeTarget(), (path, _) => path, data).Where(x => !x.Rule.Skipping).ToArray();
    }

    // 按键去重——单成员直通；多个按叠加优先级排序取顶（物化）、其余抑制；同层平局不可解。
    private static List<LockData.LockedPackage> Arbitrate(
        IEnumerable<LockData.LockedPackage> items,
        Func<LockData.LockedPackage, string> keyOf,
        Func<string, LockData.LockedPackage, string> subjectOf,
        LockData data)
    {
        var result = new List<LockData.LockedPackage>();

        foreach (var group in items.GroupBy(keyOf, StringComparer.OrdinalIgnoreCase))
        {
            var members = group.ToList();
            if (members.Count == 1)
            {
                result.Add(members[0]);
                continue;
            }

            var ranked = members.Select(p => (Pkg: p, Rank: RankOf(p, data))).OrderByDescending(x => x.Rank).ToList();

            var topRank = ranked[0].Rank;
            if (ranked.Count(x => x.Rank.CompareTo(topRank) == 0) > 1)
            {
                throw new PackageConflictException(subjectOf(group.Key, ranked[0].Pkg),
                [
                    .. ranked.Where(x => x.Rank.CompareTo(topRank) == 0).Select(x => x.Pkg)
                ]);
            }

            result.Add(ranked[0].Pkg);
        }

        return result;
    }

    // NOTE: (Tier, Index)：手动 3 > SourceOrders 列出的 2（末位最高）> 未列出的非整合包 1 >
    //  当前整合包（Setup.Source）0。列进 SourceOrders 即声明显式叠加层。
    private static (int Tier, int Index) RankOf(LockData.LockedPackage p, LockData data)
    {
        if (p.Source == null)
        {
            return (3, 0);
        }

        for (var i = 0; i < data.PackageSourceOrders.Count; i++)
        {
            if (data.PackageSourceOrders[i] == p.Source) return (2, i);
        }

        return p.Source == data.PackageSource ? (0, 0) : (1, 0);
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
