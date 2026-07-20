using System.Reactive.Disposables;
using System.Reactive.Subjects;
using TridentCore.Abstractions.Reactive;

namespace TridentCore.Abstractions.Tasks;

public abstract class TrackerBase(
    string key,
    Func<TrackerBase, Task> handler,
    Action<TrackerBase>? onCompleted,
    CancellationToken token = default
) : IDisposableLifetime
{
    private readonly CancellationTokenSource _tokenSource =
        CancellationTokenSource.CreateLinkedTokenSource(token);
    private readonly Subject<TrackerProgress> _progressSubject = new();

    public string Key => key;
    public CancellationToken Token => _tokenSource.Token;
    public TrackerState State { get; private set; } = TrackerState.Idle;
    public Exception? FailureReason { get; private set; }

    /// <summary>该 Tracker 对应的实例活动类型。</summary>
    public abstract InstanceState Kind { get; }

    /// <summary>当前归一化进度（快照）。</summary>
    public TrackerProgress Progress { get; private set; } = new TrackerProgress.Indeterminate(null);

    /// <summary>归一化进度变化流，由子类通过 <see cref="ReportProgress" /> 驱动。</summary>
    public IObservable<TrackerProgress> ProgressChanged => _progressSubject;

    public DateTimeOffset StartedAt { get; private set; } = DateTimeOffset.Now;

    /// <summary>子类调用以报告归一化进度。</summary>
    protected void ReportProgress(TrackerProgress progress)
    {
        Progress = progress;
        _progressSubject.OnNext(progress);
    }

    #region IDisposableLifetime Members

    public CompositeDisposable DisposableLifetime { get; } = new();

    public virtual void Dispose()
    {
        _progressSubject.Dispose();
        _tokenSource.Dispose();
        DisposableLifetime.Dispose();
    }

    #endregion

    public event TrackerStateUpdatedHandler? StateUpdated;

    public void Abort() => _tokenSource.Cancel();

    public void Start() => OnStart();

    protected virtual void OnStart()
    {
        State = TrackerState.Running;
        StateUpdated?.Invoke(this, State);
        _ = RunCoreAsync();
    }

    private async Task RunCoreAsync()
    {
        try
        {
            await handler(this).ConfigureAwait(false);
            if (Token.IsCancellationRequested)
            {
                OnFault(new OperationCanceledException());
            }
            else
            {
                OnFinish();
            }
        }
        catch (Exception ex)
        {
            OnFault(ex);
        }
    }

    protected virtual void OnFinish()
    {
        _progressSubject.OnCompleted();
        State = TrackerState.Finished;
        StateUpdated?.Invoke(this, State);
        onCompleted?.Invoke(this);
    }

    protected virtual void OnFault(Exception e)
    {
        _progressSubject.OnCompleted();
        FailureReason = e;
        State = TrackerState.Faulted;
        StateUpdated?.Invoke(this, State);
        onCompleted?.Invoke(this);
    }
}
