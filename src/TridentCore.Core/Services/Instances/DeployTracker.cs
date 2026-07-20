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
    ///     原生进度流，发布 (已下载, 总数) 文件计数。消费方优先订阅
    ///     <see cref="TrackerBase.ProgressChanged" />。
    /// </summary>
    public Subject<(int Current, int Total)> ProgressStream { get; } = new();

    public Subject<DeployStage> StageStream { get; } = new();

    public DeployStage CurrentStage { get; internal set; } = DeployStage.LoadLock;

    protected override void OnStart()
    {
        StageStream.Subscribe(stage =>
            ReportProgress(new TrackerProgress.Indeterminate(stage.ToString())));
        ProgressStream.Subscribe(x =>
            ReportProgress(
                new TrackerProgress.Determinate(CurrentStage.ToString(), (double)x.Current / x.Total)
            ));
        base.OnStart();
    }

    protected override void OnFinish()
    {
        StageStream.OnCompleted();
        ProgressStream.OnCompleted();
        base.OnFinish();
    }

    protected override void OnFault(Exception e)
    {
        StageStream.OnCompleted();
        ProgressStream.OnCompleted();
        base.OnFault(e);
    }

    public override void Dispose()
    {
        base.Dispose();
        ProgressStream.Dispose();
        StageStream.Dispose();
    }
}
