using TridentCore.Abstractions;
using TridentCore.Abstractions.Tasks;
using TridentCore.Core.Engines.Deploying;
using TridentCore.Core.Engines.Launching;

namespace TridentCore.Core.Services.Instances;

/// <summary>
///     一次实例活动的不可变快照。<see cref="InstanceManager" /> 每次状态变化都发布一个新值，
///     消费方只持有值、不持有活动对象——因此不存在「拿到的东西已被释放」这类问题。
/// </summary>
/// <remarks>
///     进度与终态字段都在值里带全，消费方（通知、活动记录、崩溃诊断）无需回查活动对象即可
///     在完成时读出所需信息。
/// </remarks>
public abstract record InstanceActivity
{
    public required string Key { get; init; }

    /// <summary>
    ///     一次运行的标识。
    /// </summary>
    /// <remarks>
    ///     值语义下同一次运行的两个快照相等、不同次运行的快照可能字段全同，引用比较不再可用；
    ///     消费方靠它区分「同一次运行的进度更新」与「换了一次新运行」。
    /// </remarks>
    public required Guid Id { get; init; }

    public ActivityState State { get; init; } = ActivityState.Running;

    public ActivityProgress Progress { get; init; } = new ActivityProgress.Indeterminate(null);

    public DateTimeOffset StartedAt { get; init; } = DateTimeOffset.Now;
    public DateTimeOffset? CompletedAt { get; init; }

    /// <summary>失败原因，仅 <see cref="ActivityState.Faulted" /> 时非空。</summary>
    public Exception? FailureReason { get; init; }

    /// <summary>活动类型，用于 UI 展示实例当前在做什么。</summary>
    public abstract InstanceState Kind { get; }

    public bool IsCompleted => State is not ActivityState.Running;

    #region Nested type: Installing

    public sealed record Installing : InstanceActivity
    {
        public override InstanceState Kind => InstanceState.Installing;

        /// <summary>安装来源的 pref，解析完成后才有值。</summary>
        public string? Reference { get; init; }
    }

    #endregion

    #region Nested type: Updating

    public sealed record Updating : InstanceActivity
    {
        public override InstanceState Kind => InstanceState.Updating;

        public string? OldSource { get; init; }
        public string? NewSource { get; init; }
    }

    #endregion

    #region Nested type: Deploying

    public sealed record Deploying : InstanceActivity
    {
        public override InstanceState Kind => InstanceState.Deploying;

        public DeployStage CurrentStage { get; init; } = DeployStage.LoadLock;

        /// <summary>当前阶段的文件计数，仅下载阶段有值。</summary>
        public (int Current, int Total)? FileCount { get; init; }
    }

    #endregion

    #region Nested type: Running

    public sealed record Running : InstanceActivity
    {
        public override InstanceState Kind => InstanceState.Running;

        public string? AccountId { get; init; }
        public uint MaxMemory { get; init; }
        public int? ProcessId { get; init; }
        public DateTimeOffset? RunStartedAt { get; init; }
        public LaunchOutcome? Outcome { get; init; }
        public int? ExitCode { get; init; }

        public string? JavaHome { get; init; }
        public uint? JavaVersion { get; init; }
        public string? CommandLine { get; init; }
    }

    #endregion
}
