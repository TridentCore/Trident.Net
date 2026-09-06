namespace TridentCore.Abstractions.Launching;

public sealed record LaunchDefinition
{
    public int FormatVersion { get; init; } = 1;
    public required IReadOnlyList<LaunchSelection> Components { get; init; }
}
