using Microsoft.Extensions.Logging;
using TridentCore.Abstractions.FileModels;
using TridentCore.Abstractions.Repositories;
using TridentCore.Abstractions.Repositories.Resources;
using TridentCore.Abstractions.Utilities;
using TridentCore.Core.Services;
using TridentCore.Core.Utilities;
using TridentCore.Purl;

namespace TridentCore.Core.Engines.Deploying;

public class PackagePlanner(ILogger<PackagePlanner> logger, RepositoryAgent agent)
{
    // Standalone planning API consumed by exporters and the host's materialization flows. Resolves
    // + evaluates rules + materializes a target path into a PackagePlan. The deploy pipeline does
    // not use this — it uses ResolveAsync / EvaluateRule / RecomputeRule directly against the lock.
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
            if (!PackageHelper.TryParse(entry.Purl, out var parsed))
            {
                throw new FormatException($"Package {entry.Purl} is not a valid package");
            }

            index.Add((new(parsed.Label, parsed.Namespace, parsed.Pid, parsed.Vid), entry));
        }

        if (index.Count == 0)
        {
            return [];
        }

        var resolved = await agent
            .ResolveBatchAsync(index.Select(x => x.Key), filter)
            .ConfigureAwait(false);

        var byKey = index.ToDictionary(x => x.Key, x => x.Origin);
        return resolved
            .Where(x => byKey.ContainsKey(x.Item1))
            .Select(x => (byKey[x.Item1], x.Item2))
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

    // Pure rule recompute from a cached resolution — zero network, zero re-resolve. Used by
    // SyncPackages' fine-grained rule invalidation so a rule tweak never drifts floating purls.
    public LockData.PackageRule RecomputeRule(
        Profile.Rice.Entry entry,
        LockData.ResolvedPackage resolved,
        IReadOnlyList<Profile.Rice.Rule> rules
    )
    {
        var package = ReconstructPackage(entry.Purl, resolved);
        return EvaluateRule(entry, package, rules);
    }

    private LockData.PackageRule ToPackageRule(RuleHelper.Result result, Profile.Rice.Entry entry)
    {
        if (result is { Matched: true, EffectiveRule: { } effectiveRule })
        {
            logger.LogDebug("Rule {{ {skipping}, {destination} }} applied to {purl}",
                            effectiveRule.Skipping,
                            effectiveRule.Destination ?? "<default>",
                            entry.Purl);

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
        var fileName = rule.Normalizing
            ? string.Concat(FileHelper.Sanitize(package.ProjectName), Path.GetExtension(package.FileName))
            : package.FileName;
        var relativeTarget = rule.Destination is not null
            ? Path.Combine(rule.Destination, fileName)
            : Path.Combine(FileHelper.GetAssetFolderName(package.Kind), fileName);

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

    // Rebuilds a Package just sufficient for RuleHelper evaluation from the cached resolution +
    // the declared purl (which carries Namespace/ProjectId the resolution does not). Only the
    // fields rule selectors read are populated meaningfully; the rest are inert placeholders.
    private static Package ReconstructPackage(string purl, LockData.ResolvedPackage resolved)
    {
        PackageHelper.TryParse(purl, out var parsed);
        return new(
            Label: resolved.Label,
            Namespace: parsed.Namespace,
            ProjectId: parsed.Pid,
            VersionId: resolved.Vid,
            ProjectName: resolved.ProjectName,
            VersionName: resolved.Vid,
            Thumbnail: null,
            Author: string.Empty,
            Summary: string.Empty,
            Reference: new("sourced://recompute", UriKind.Absolute),
            Kind: resolved.Kind,
            ReleaseType: ReleaseType.Release,
            PublishedAt: DateTimeOffset.UnixEpoch,
            Download: resolved.Url,
            Size: (ulong)resolved.Size,
            FileName: resolved.FileName,
            Hash: resolved.Hashes.Primary,
            Requirements: new([], []),
            Dependencies: []
        );
    }
}
