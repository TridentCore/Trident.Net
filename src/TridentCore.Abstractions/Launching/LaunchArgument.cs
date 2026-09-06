namespace TridentCore.Abstractions.Launching;

public sealed record LaunchArgument(string Value, IReadOnlyList<LaunchRule>? Rules = null);
