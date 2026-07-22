using TridentCore.Abstractions.FileModels;

namespace TridentCore.Core.Exceptions;

// Thrown by FlattenPackages when two or more packages share the SAME top priority tier for one
// arbitration key (project or path) — the engine refuses to silently pick a winner. A same-tier
// tie is a setup mistake the user must resolve by ordering SourceOrders or removing the
// duplicate. Cross-tier conflicts are always decidable and never reach here.
public class PackageConflictException(string subject, IReadOnlyList<LockData.LockedPackage> collisions)
    : Exception($"Unresolvable package conflict on {subject}: "
              + $"{collisions.Count} packages share the top priority — "
              + $"{string.Join(", ", collisions.Select(c => $"{c.Pref} [{c.Source ?? "manual"}]"))}. "
              + $"Reorder them in SourceOrders or remove duplicates.")
{
    public string Subject { get; } = subject;

    public IReadOnlyList<LockData.LockedPackage> Collisions { get; } = collisions;
}
