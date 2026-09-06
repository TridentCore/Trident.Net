namespace TridentCore.Abstractions.Launching;

public sealed record LaunchComponent
{
    public int FormatVersion { get; init; } = 1;
    public required string Id { get; init; }
    public string? Version { get; init; }
    public IReadOnlyList<LaunchRequirement> Requires { get; init; } = [];
    public IReadOnlyList<LaunchRequirement> Conflicts { get; init; } = [];
    public string? MainClass { get; init; }
    public bool? StartOnFirstThread { get; init; }
    public IReadOnlyList<uint>? JavaMajors { get; init; }
    public LaunchAssetIndex? AssetIndex { get; init; }
    public LaunchArguments GameArguments { get; init; } = new();
    public LaunchArguments JavaArguments { get; init; } = new();
    public IReadOnlyList<LaunchLibrary> Libraries { get; init; } = [];
}
