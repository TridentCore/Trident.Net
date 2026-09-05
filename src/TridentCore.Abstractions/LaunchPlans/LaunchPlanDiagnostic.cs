using JetBrains.Annotations;

namespace TridentCore.Abstractions.LaunchPlans;

[PublicAPI]
public sealed record LaunchPlanDiagnostic(LaunchPlanDiagnostic.Kind Level, string Message, string? Path = null)
{
    public enum Kind
    {
        Warning,
        Error
    }
}
