namespace TridentCore.Abstractions.Launching;

public sealed record LaunchTarget(string Os, string Architecture, string OsVersion, uint JavaMajor);
