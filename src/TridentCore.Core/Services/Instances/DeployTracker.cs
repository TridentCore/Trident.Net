using System.Reactive.Subjects;
using TridentCore.Abstractions;
using TridentCore.Abstractions.Tasks;
using TridentCore.Core.Engines.Deploying;

namespace TridentCore.Core.Services.Instances;

public class DeployTracker(
    string key,
    Func<TrackerBase, Task> handler,
    Action<TrackerBase>? onCompleted = null,
    CancellationToken token = default
) : TrackerBase(key, handler, onCompleted, token)
{
    public override InstanceState Kind => InstanceState.Deploying;

    /// <summary>
    ///     原生进度流，取值 0.0–1.0 或 null（当前部署阶段不可量化）。消费方优先订阅
    ///     <see cref="TrackerBase.ProgressChanged" />。
    /// </summary>
    public Subject<double?> ProgressStream { get; } = new();

    public Subject<DeployStage> StageStream { get; } = new();

    public DeployStage CurrentStage { get; internal set; } = DeployStage.CheckArtifact;

    protected override void OnStart()
    {
        StageStream.Subscribe(stage =>
            ReportProgress(new TrackerProgress.Indeterminate(stage.ToString())));
        ProgressStream.Subscribe(p =>
            ReportProgress(p.HasValue
                ? new TrackerProgress.Determinate(CurrentStage.ToString(), p.Value)
                : new TrackerProgress.Indeterminate(CurrentStage.ToString())));
        base.OnStart();
    }

    public override void Dispose()
    {
        base.Dispose();
        ProgressStream.Dispose();
        StageStream.Dispose();
    }
}
