namespace TridentCore.Abstractions.Launching;

public sealed record LaunchRule(bool Allow, string? Os = null, string? Architecture = null, string? OsVersion = null);
