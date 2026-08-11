using TridentCore.Abstractions.FileModels;

namespace TridentCore.Core.Exceptions;

// NOTE: FlattenPackages 在两个及以上包共享同一仲裁键（project 或 path）的最高优先级层时抛出——
//  引擎拒绝静默选赢家。同层平局是用户设置错误，须重排 SourceOrders 或删重复项解决；
//  跨层冲突恒可裁决，不会走到这里。
public class PackageConflictException(string subject, IReadOnlyList<LockData.LockedPackage> collisions)
    : Exception($"Unresolvable package conflict on {subject}: "
              + $"{collisions.Count} packages share the top priority — "
              + $"{string.Join(", ", collisions.Select(c => $"{c.Pref} [{c.Source ?? "manual"}]"))}. "
              + $"Reorder them in SourceOrders or remove duplicates.")
{
    public string Subject { get; } = subject;

    public IReadOnlyList<LockData.LockedPackage> Collisions { get; } = collisions;
}
