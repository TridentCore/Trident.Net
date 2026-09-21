using System.Text.Json.Serialization;

namespace TridentCore.Core.Engines.Deploying;

public sealed record PersistProjectionManifest
{
    public const int CURRENT_VERSION = 1;

    [JsonRequired]
    public int Version { get; init; } = CURRENT_VERSION;
    [JsonRequired]
    public IReadOnlyList<FileEntry> Files { get; init; } = [];

    public sealed record FileEntry(string Path, long LastWriteTimeUtcTicks);
}
