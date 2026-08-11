using TridentCore.Abstractions.Extensions;
using TridentCore.Abstractions.FileModels;
using TridentCore.Abstractions.Repositories;
using TridentCore.Abstractions.Repositories.Resources;
using TridentCore.Abstractions.Utilities;

namespace TridentCore.Core.Engines.Deploying.Stages;

// NOTE: 按 pref 在 BaseLock（真源）与 Lock（产物）之间同步包，绝不重解析已锁 vid 的包。
//  规则变更对缓存解析结果离线重算；仅 fingerprint 变化的 floating pref（或全新 pref）才
//  命中仓库——规则微调永远漂移不了已锁版本。
public class SyncPackagesStage(PackagePlanner planner) : StageBase
{
    protected override async Task OnProcessAsync(CancellationToken token)
    {
        var setup = Context.Setup;
        var enabled = setup.Packages.Where(x => x.Enabled).ToList();
        var rules = setup.Rules.Where(x => x.Enabled).ToList();
        var baseLock = Context.BaseLock;

        var filter = Filter.FromSetup(setup);

        // NOTE: Step 1——按 (project, source) 身份做 diff。vid 有意忽略，fixed→floating 翻转仍能匹配
        //  并继承已锁解析；含 source 使同项目来自不同层（整合包/手动/recipe）各自存活到
        //  FlattenPackages，由它按叠加优先级裁决同目标冲突。
        var setupByKey = new Dictionary<Key, Profile.Rice.Entry>();
        foreach (var entry in enabled)
        {
            setupByKey[MatchKey(entry.Pref, entry.Source)] = entry;
        }

        var baseByKey = new Dictionary<Key, LockData.LockedPackage>();
        if (baseLock != null)
        {
            foreach (var locked in baseLock.Packages)
            {
                baseByKey[MatchKey(locked.Pref, locked.Source)] = locked;
            }
        }

        // NOTE: Removed 桶（BaseLock 有、Setup 无）不迁移——无事可做。

        // NOTE: floating 解析的 filter 只依赖 platform(Version/Loader)，不依赖 deploy options——
        //  options 变更走 Verify(重部署门)，不在这里触发 floating 重解析。
        var platformChanged = baseLock == null || baseLock.Platform != Context.Lock.Platform;

        var result = new List<LockData.LockedPackage>();
        var toResolve = new List<Profile.Rice.Entry>();

        // NOTE: Steps 2 & 3——逐包判定已解析有效性 + 对匹配项离线重算规则。
        var matchedKeys = setupByKey.Keys.Intersect(baseByKey.Keys).ToList();
        foreach (var key in matchedKeys)
        {
            var entry = setupByKey[key];
            var locked = baseByKey[key];

            var parsed = PackageHelper.Parse(entry.Pref);
            var floating = parsed.Version == null;
            // NOTE: floating pref 在 platform/options fingerprint 变化时失效（解析依赖 filter）；
            //  fixed pref 保持 vid，除非用户显式重钉（vid 与锁定不同）——尊重意图。
            var resolvedInvalid = floating
                                      ? platformChanged
                                      : !string.Equals(parsed.Version,
                                                       locked.Resolved.VersionId,
                                                       StringComparison.InvariantCulture);
            if (resolvedInvalid)
            {
                // NOTE: filter/策略变化或用户重定固定版本 → 重新解析。
                toResolve.Add(entry);
            }
            else
            {
                var rule = planner.EvaluateRule(entry, locked.Resolved, rules);
                // NOTE: SuppressedBy 只由 FlattenPackages 仲裁；此处匹配时重置，
                //  使后来成为唯一占位的输家被重新激活，不留过期赢家指针。
                result.Add(locked with { Pref = entry.Pref, Source = entry.Source, Rule = rule, SuppressedBy = null });
            }
        }

        // NOTE: Added 桶——Setup 有、BaseLock 无 → 解析。
        foreach (var key in setupByKey.Keys.Except(baseByKey.Keys))
        {
            toResolve.Add(setupByKey[key]);
        }

        // NOTE: Step 4——解析（网络）无效项与新增项，然后组装。
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

    private LockData.LockedPackage BuildLocked(
        Profile.Rice.Entry entry,
        Package package,
        IReadOnlyList<Profile.Rice.Rule> rules)
    {
        var rule = planner.EvaluateRule(entry, package, rules);
        return new(entry.Pref, entry.Source, package, rule);
    }

    private static Key MatchKey(string pref, string? source)
    {
        var parsed = PackageHelper.Parse(pref);
        return new(parsed.Repository.ToLowerInvariant(), parsed.Namespace ?? string.Empty, parsed.Identity, source);
    }

    private record Key(string Label, string Namespace, string Pid, string? Source);
}
