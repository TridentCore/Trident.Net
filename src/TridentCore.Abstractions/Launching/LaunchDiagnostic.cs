namespace TridentCore.Abstractions.Launching;

public sealed record LaunchDiagnostic(LaunchDiagnostic.Kind Level, string Message, string? Path = null)
{
    public enum Kind { Warning, Error }
}
