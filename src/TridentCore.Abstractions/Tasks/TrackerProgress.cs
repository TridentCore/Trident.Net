namespace TridentCore.Abstractions.Tasks;

/// <summary>
///     归一化的进度表达。三种态，覆盖所有 Tracker：
///     <list type="bullet">
///         <item><see cref="None" /> —— 无进度概念（如游戏运行中，非「朝终点推进」的任务），消费方应隐藏进度条</item>
///         <item>
///             <see cref="Indeterminate" /> —— 有进度条但脉冲（不可量化阶段，如校验、构建产物）
///         </item>
///         <item>
///             <see cref="Determinate" /> —— 精确百分比，<see cref="Determinate.Percent" /> 取值 0.0–1.0
///         </item>
///     </list>
///     这是数据模型而非视图模型，消费方自行桥接到绑定属性。
/// </summary>
public abstract record TrackerProgress
{
    private TrackerProgress() { }

    /// <summary>无进度概念。消费方应隐藏进度条，改用运行指示器。</summary>
    public sealed record None : TrackerProgress;

    /// <summary>有进度条但脉冲（indeterminate）。Stage 为子阶段名，可空。</summary>
    public sealed record Indeterminate(string? Stage) : TrackerProgress;

    /// <summary>精确百分比进度。Percent 取值 0.0–1.0。Stage 为子阶段名，可空。</summary>
    public sealed record Determinate(string? Stage, double Percent) : TrackerProgress;
}
