namespace TridentCore.Abstractions.Tasks;

/// <summary>
///     通用进度表达，计量单位由活动决定：
///     <list type="bullet">
///         <item><see cref="None" /> —— 无进度概念（如游戏运行中），消费方应隐藏进度条</item>
///         <item><see cref="Indeterminate" /> —— 有进度条但脉冲（不可量化阶段）</item>
///         <item><see cref="Determinate" /> —— 当前工作量与总工作量</item>
///     </list>
///     这是数据模型而非视图模型，消费方自行桥接到绑定属性。
/// </summary>
public abstract record ActivityProgress
{
    private ActivityProgress() { }

    /// <summary>无进度概念。消费方应隐藏进度条，改用运行指示器。</summary>
    public sealed record None : ActivityProgress;

    /// <summary>有进度条但脉冲（indeterminate）。Stage 为子阶段名，可空。</summary>
    public sealed record Indeterminate(string? Stage) : ActivityProgress;

    /// <summary>有级进度，0 ≤ Current ≤ Total 且 Total > 0。Stage 为子阶段名，可空。</summary>
    public sealed record Determinate(string? Stage, double Current, double Total) : ActivityProgress;
}
