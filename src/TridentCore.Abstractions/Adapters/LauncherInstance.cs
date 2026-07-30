namespace TridentCore.Abstractions.Adapters;

public record LauncherInstance
{
    public required LauncherKind Kind { get; init; }

    // The instance's directory name under the launcher root; a candidate for the Trident instance key.
    public required string Key { get; init; }

    // Absolute path of the instance home (where the launcher's metadata files live).
    public required string HomeDirectory { get; init; }

    public string? Name { get; init; }
    public string? MinecraftVersion { get; init; }

    // Loader as a Trident lurl, or null for vanilla.
    public string? Loader { get; init; }

    // Non-null when the instance metadata could not be fully parsed; the value is the failure reason.
    public CorruptReason? CorruptReason { get; init; }

    // Absolute path of the runtime directory (.minecraft equivalent) — the copy source for build/.
    public required string RuntimeDirectory { get; init; }

    // Subdirectory names under RuntimeDirectory whose files participate in batch identification
    // (e.g. mods, resourcepacks, shaderpacks). Hit files become package references; the rest are copied.
    public required IReadOnlyList<string> IdentifiableSubdirs { get; init; }
}
