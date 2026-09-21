using System.Text.Json.Serialization;

namespace TridentCore.Core.Engines.Deploying;

public sealed record ImportProjectionManifest
{
    public const int CURRENT_VERSION = 1;

    [JsonRequired]
    public int Version { get; init; } = CURRENT_VERSION;
    [JsonRequired]
    public IReadOnlyList<string> Files { get; init; } = [];
}
