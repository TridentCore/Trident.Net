using TridentCore.Abstractions;
using TridentCore.Abstractions.FileModels;
using TridentCore.Abstractions.Repositories;
using TridentCore.Abstractions.Repositories.Resources;
using TridentCore.Abstractions.Utilities;

namespace TridentCore.Core.Engines.Deploying.Stages;

// Synchronizes packages between BaseLock (truth) and Lock (product) by purl, never re-resolving
// a package whose vid is already locked. Rule changes are recomputed offline against the cached
// resolution; only floating purls whose platform/options fingerprint changed (or brand-new purls)
// hit the repositories — so a rule tweak can never drift the locked versions.
public class SyncPackagesStage(PackagePlanner planner) : StageBase
{
    protected override async Task OnProcessAsync(CancellationToken token)
    {
        var setup = Context.Setup;
        var enabled = setup.Packages.Where(x => x.Enabled).ToList();
        var rules = setup.Rules.Where(x => x.Enabled).ToList();
        var baseLock = Context.BaseLock;

        var filter = BuildFilter(setup);

        // Step 1: diff view keyed by project identity (vid intentionally ignored so a fixed→floating
        // flip still matches and inherits the locked resolution).
        var setupByKey = new Dictionary<Key, Profile.Rice.Entry>();
        foreach (var entry in enabled)
        {
            setupByKey[MatchKey(entry.Purl)] = entry;
        }

        var baseByKey = new Dictionary<Key, LockData.LockedPackage>();
        if (baseLock != null)
        {
            foreach (var locked in baseLock.Packages)
            {
                baseByKey[MatchKey(locked.Purl)] = locked;
            }
        }

        // Removed桶 (in BaseLock, not in Setup) are simply not migrated — nothing to do.

        // NOTE: floating 解析的 filter 只依赖 platform(Version/Loader)，不依赖 deploy options——
        //  options 变更走 Verify(重部署门)，不在这里触发 floating 重解析。
        var platformChanged = baseLock == null
            || baseLock.Platform != Context.Lock.Platform;

        var result = new List<LockData.LockedPackage>();
        var toResolve = new List<Profile.Rice.Entry>();

        // Steps 2 & 3: per-package resolved validity + offline rule recompute for matched.
        var matchedKeys = setupByKey.Keys.Intersect(baseByKey.Keys).ToList();
        foreach (var key in matchedKeys)
        {
            var entry = setupByKey[key];
            var locked = baseByKey[key];

            PackageHelper.TryParse(entry.Purl, out var parsed);
            var floating = parsed.Vid == null;
            // Floating purls invalidate when the platform/options fingerprint changed (the
            // resolution was filter-dependent). Fixed purls keep their vid unless the user
            // explicitly repinned it (vid differs from the locked one) — honoring intent.
            var resolvedInvalid = floating
                ? platformChanged
                : !string.Equals(parsed.Vid, locked.Resolved.Vid, StringComparison.OrdinalIgnoreCase);
            if (resolvedInvalid)
            {
                // filter/策略变了，或用户重定了固定版本 → 重新解析
                toResolve.Add(entry);
            }
            else
            {
                var rule = planner.RecomputeRule(entry, locked.Resolved, rules);
                result.Add(locked with { Purl = entry.Purl, Source = entry.Source, Rule = rule });
            }
        }

        // Added桶: Setup 有、BaseLock 无 → 解析
        foreach (var key in setupByKey.Keys.Except(baseByKey.Keys))
        {
            toResolve.Add(setupByKey[key]);
        }

        // Step 4: resolve (network) the invalid + added entries, then assemble.
        if (toResolve.Count > 0)
        {
            var resolved = await planner.ResolveAsync(toResolve, filter).ConfigureAwait(false);
            foreach (var (entry, package) in resolved)
            {
                result.Add(BuildLocked(entry, package, rules));
            }
        }

        if (token.IsCancellationRequested)
        {
            return;
        }

        Context.Lock = Context.Lock with { Packages = result };
    }

    private static Filter BuildFilter(Profile.Rice setup)
    {
        string? loader = null;
        if (setup.Loader != null)
        {
            if (LoaderHelper.TryParse(setup.Loader, out var result))
            {
                loader = result.Identity;
            }
            else
            {
                throw new FormatException($"{setup.Loader} is not well formatted loader string");
            }
        }

        return new(setup.Version, loader, null);
    }

    private LockData.LockedPackage BuildLocked(
        Profile.Rice.Entry entry,
        Package package,
        IReadOnlyList<Profile.Rice.Rule> rules
    )
    {
        var rule = planner.EvaluateRule(entry, package, rules);
        return new(
                   entry.Purl,
                   entry.Source,
                   new(
                       package.VersionId,
                       package.Label,
                       package.Kind,
                       package.ProjectName,
                       package.FileName,
                       package.Download,
                       (long)package.Size,
                       LockData.FileHashes.From(package.Hash)
                      ),
                   rule,
                   new(
                       package.Requirements.AnyOfVersions.ToList(),
                       package.Requirements.AnyOfLoaders.ToList()
                      )
                  );
    }

    private static Key MatchKey(string purl)
    {
        PackageHelper.TryParse(purl, out var parsed);
        return new(
            (parsed.Label ?? string.Empty).ToLowerInvariant(),
            parsed.Namespace ?? string.Empty,
            parsed.Pid ?? string.Empty
        );
    }

    private record Key(string Label, string Namespace, string Pid);
}
