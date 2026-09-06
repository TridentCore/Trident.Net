using TridentCore.Abstractions.Launching;
using TridentCore.Abstractions.Repositories.Resources;

namespace TridentCore.Abstractions.FileModels;

public record LockData
{
    public required PlatformData Platform { get; init; }
    public CompiledLaunch? Launch { get; init; }
    public IReadOnlyList<LockedPackage> Packages { get; init; } = [];
    public RuntimeData? Runtime { get; init; }

    public sealed record PlatformData(string Minecraft, string? Loader);

    public sealed record LockedPackage(
        string Pref,
        string? Source,
        Package Resolved,
        PackageRule Rule,
        string? SuppressedBy = null);

    public sealed record PackageRule(bool Skipping, string? Destination, bool Normalizing);

    public sealed record RuntimeData(uint Major, string Sha1, string Os, string Architecture);
}
