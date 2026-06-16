using System.Reactive.Subjects;
using TridentCore.Abstractions;
using TridentCore.Abstractions.Tasks;

namespace TridentCore.Core.Services.Instances;

public class UpdateTracker(
    string key,
    Func<TrackerBase, Task> handler,
    Action<TrackerBase>? onCompleted,
    CancellationToken token = default
) : TrackerBase(key, handler, onCompleted, token)
{
    public override InstanceState Kind => InstanceState.Updating;

    /// <summary>原生进度流，取值 0.0–1.0 或 null（不可量化阶段）。消费方优先订阅 <see cref="TrackerBase.ProgressChanged" />。</summary>
    public Subject<double?> ProgressStream { get; } = new();

    public string? OldSource { get; set; }
    public string? NewSource { get; set; }

    protected override void OnStart()
    {
        ProgressStream.Subscribe(p =>
            ReportProgress(p.HasValue
                ? new TrackerProgress.Determinate(null, p.Value)
                : new TrackerProgress.Indeterminate(null))
        );
        base.OnStart();
    }

    public override void Dispose()
    {
        base.Dispose();
        ProgressStream.Dispose();
    }
}
