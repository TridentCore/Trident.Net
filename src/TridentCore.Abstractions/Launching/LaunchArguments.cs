namespace TridentCore.Abstractions.Launching;

public sealed record LaunchArguments
{
    public IReadOnlyList<LaunchArgument>? Replace { get; init; }
    public IReadOnlyList<LaunchArgument> Append { get; init; } = [];
}
