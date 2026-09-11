namespace TridentCore.Abstractions.FileModels;

public record PatchArtifact
{
    public required string MainClass { get; init; }
    public required uint JavaMajor { get; init; }
    public IReadOnlyList<string[]> GameArguments { get; init; } = [];
    public IReadOnlyList<string[]> JvmArguments { get; init; } = [];
    public bool DefaultJvmArguments { get; init; } = true;
    public IReadOnlyList<PatchLibrary> Libraries { get; init; } = [];
    public IReadOnlyList<PatchAgent> Agents { get; init; } = [];
    public PatchLibrary? MainJar { get; init; }
    public required LockData.AssetData AssetIndex { get; init; }
}
