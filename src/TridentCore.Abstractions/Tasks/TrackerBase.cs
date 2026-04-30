using System.Reactive.Disposables;
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
    public string Key => key;
    public CancellationToken Token => _tokenSource.Token;
    public TrackerState State { get; private set; } = TrackerState.Idle;
    public Exception? FailureReason { get; private set; }

    public DateTimeOffset StartedAt { get; private set; } = DateTimeOffset.Now;

    #region IDisposableLifetime Members

    public CompositeDisposable DisposableLifetime { get; } = new();

    public virtual void Dispose()
    {
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
        _ = RunInternalAsync();
    }

    private async Task RunInternalAsync()
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
        State = TrackerState.Finished;
        StateUpdated?.Invoke(this, State);
        onCompleted?.Invoke(this);
    }

    protected virtual void OnFault(Exception e)
    {
        FailureReason = e;
        State = TrackerState.Faulted;
        StateUpdated?.Invoke(this, State);
        onCompleted?.Invoke(this);
    }
}
