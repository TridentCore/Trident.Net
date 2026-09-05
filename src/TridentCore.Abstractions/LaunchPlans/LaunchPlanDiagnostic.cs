using JetBrains.Annotations;

namespace TridentCore.Abstractions.LaunchPlans;

// 外部格式与原生计划互转时产生的诊断。Warning 表示语义有偏差但结果可用；
// Error 表示某个启动意图在目标格式里无法表达，产物行为与源实例不一致。
[PublicAPI]
public sealed record LaunchPlanDiagnostic(LaunchPlanDiagnostic.Kind Level, string Message, string? Path = null)
{
    #region Nested type: Kind

    public enum Kind
    {
        Warning,
        Error
    }

    #endregion
}
