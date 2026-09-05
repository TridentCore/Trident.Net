namespace TridentCore.Abstractions.Tasks;

/// <summary>
///     一次实例活动的生命周期状态。活动不存在即代表空闲，故此处没有 Idle。
/// </summary>
public enum ActivityState
{
    Running,
    Finished,

    /// <summary>因异常终止，<c>FailureReason</c> 必然非空。</summary>
    Faulted,

    /// <summary>被主动中止。与 Faulted 分开，使「用户取消」不必靠异常类型反推。</summary>
    Cancelled
}
