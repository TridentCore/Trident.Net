namespace TridentCore.Abstractions.Launching;

public sealed record LaunchRequirement(string Id, string? Version = null, string? SuggestedVersion = null);
