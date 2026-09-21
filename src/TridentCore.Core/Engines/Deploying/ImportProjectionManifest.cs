namespace TridentCore.Core.Engines.Deploying;

public sealed record ImportProjectionManifest
{
    public const int CURRENT_VERSION = 1;

    public int Version { get; init; } = CURRENT_VERSION;
    public IReadOnlyList<string> Files { get; init; } = [];
}
