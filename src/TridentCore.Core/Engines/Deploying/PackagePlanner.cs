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
    // Standalone planning API consumed by exporters and the host's materialization flows. Resolves
    // + evaluates rules + materializes a target path into a PackagePlan. The deploy pipeline does
    // not use this — it uses ResolveAsync / EvaluateRule directly against the lock.
    public async IAsyncEnumerable<PackagePlan> PlanAsync(
        IReadOnlyList<Profile.Rice.Entry> packages,
        PackagePlannerContext context
    )
    {
        var resolved = await ResolveAsync(packages, context.Filter).ConfigureAwait(false);
        foreach (var (entry, package) in resolved)
        {
            var rule = EvaluateRule(entry, package, context.Rules);
            yield return ToPlan(entry, package, rule);
        }
    }

    // Network resolve: batch-resolves the supplied entries against the repositories.
    public async Task<IReadOnlyList<(Profile.Rice.Entry Entry, Package Package)>> ResolveAsync(
        IReadOnlyList<Profile.Rice.Entry> packages,
        Filter filter
    )
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

        var resolved = await agent
            .ResolveBatchAsync(index.Select(x => x.Key).Distinct(), filter)
            .ConfigureAwait(false);

        resolved.ThrowIfFailures();

        // Same project+version from distinct sources now coexist (SyncPackages keys on
        // (project, source)); fan a single resolution out to every entry sharing that key.
        var byKey = index.ToLookup(x => x.Key, x => x.Origin);
        return resolved.Successful
            .SelectMany(x => byKey[x.Key].Select(origin => (origin, x.Value)))
            .ToList();
    }

    // Pure rule evaluation for a freshly resolved package.
    public LockData.PackageRule EvaluateRule(
        Profile.Rice.Entry entry,
        Package package,
        IReadOnlyList<Profile.Rice.Rule> rules
    )
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

    private static PackagePlan ToPlan(
        Profile.Rice.Entry entry,
        Package package,
        LockData.PackageRule rule
    )
    {
        var relativeTarget = PackagePathHelper.RelativeTarget(
            rule.Normalizing,
            rule.Destination,
            package.ProjectName,
            package.FileName,
            package.Kind);

        return new(
            package.Label,
            package.Namespace,
            package.ProjectId,
            package.VersionId,
            relativeTarget,
            package.Download,
            package.Hash
        )
        { IsSkipping = rule.Skipping };
    }
}
