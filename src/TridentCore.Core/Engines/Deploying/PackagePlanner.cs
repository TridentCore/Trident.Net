using Microsoft.Extensions.Logging;
using TridentCore.Abstractions.FileModels;
using TridentCore.Abstractions.Repositories;
using TridentCore.Abstractions.Repositories.Resources;
using TridentCore.Abstractions.Utilities;
using TridentCore.Core.Services;
using TridentCore.Core.Utilities;
using TridentCore.Pref;

namespace TridentCore.Core.Engines.Deploying;

public class PackagePlanner(ILogger<PackagePlanner> logger, RepositoryAgent agent)
{
    // NOTE: 独立规划 API，导出器与宿主物化流程使用。解析 + 评估规则 + 物化目标路径为
    //  PackagePlan；部署管线不用它——直接对锁用 ResolveAsync/EvaluateRule。
    public async IAsyncEnumerable<PackagePlan> PlanAsync(
        IReadOnlyList<Profile.Rice.Entry> packages,
        PackagePlannerContext context)
    {
        var resolved = await ResolveAsync(packages, context.Filter).ConfigureAwait(false);
        foreach (var (entry, package) in resolved)
        {
            var rule = EvaluateRule(entry, package, context.Rules);
            yield return ToPlan(entry, package, rule);
        }
    }

    // NOTE: 网络解析——对仓库批量解析给定条目。
    public async Task<IReadOnlyList<(Profile.Rice.Entry Entry, Package Package)>> ResolveAsync(
        IReadOnlyList<Profile.Rice.Entry> packages,
        Filter filter)
    {
        var index = new List<(PackageIdentifier Key, Profile.Rice.Entry Origin)>();
        foreach (var entry in packages)
        {
            if (!PackageHelper.TryParse(entry.Pref, out var parsed))
            {
                throw new FormatException($"Package {entry.Pref} is not a valid package");
            }

            index.Add((new(parsed.Repository, parsed.Namespace, parsed.Identity, parsed.Version), entry));
        }

        if (index.Count == 0)
        {
            return [];
        }

        var resolved = await agent.ResolveBatchAsync(index.Select(x => x.Key).Distinct(), filter).ConfigureAwait(false);

        resolved.ThrowIfFailures();

        // NOTE: 同项目同版本现可来自不同源（SyncPackages 以 (project, source) 为键）；
        //  把一次解析扇出给共享该键的每个条目。
        var byKey = index.ToLookup(x => x.Key, x => x.Origin);
        return [.. resolved.Successful.SelectMany(x => byKey[x.Key].Select(origin => (origin, x.Value)))];
    }

    // NOTE: 对新解析的包做纯规则评估。
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

    private static PackagePlan ToPlan(Profile.Rice.Entry entry, Package package, LockData.PackageRule rule)
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
}
