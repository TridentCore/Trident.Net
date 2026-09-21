namespace TridentCore.Core.Engines.Deploying;

public sealed record PersistProjectionManifest
{
    public const int CURRENT_VERSION = 1;

    public int Version { get; init; } = CURRENT_VERSION;
    public IReadOnlyList<FileEntry> Files { get; init; } = [];

    public sealed record FileEntry(string Path, long LastWriteTimeUtcTicks);
}
