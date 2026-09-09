using System.Reactive.Linq;
using System.Reactive.Subjects;
using Microsoft.Extensions.Logging;
using TridentCore.Abstractions.Tasks;

namespace TridentCore.Core.Services.Instances;

/// <summary>一次逻辑操作的生命周期，可包含多个活动阶段。</summary>
internal sealed class InstanceOperation
{
    private readonly Lock _gate = new();
    private readonly CancellationTokenSource _source = new();
    private readonly TaskCompletionSource<InstanceActivity> _completion =
        new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly ReplaySubject<InstanceActivity> _subject = new(1);
    private readonly ActivityPublisher _publisher;
    private readonly Action<InstanceOperation> _release;
    private readonly ILogger _logger;
    private InstanceActivity _current;
    private Task _cancellation = Task.CompletedTask;
    private StopMode? _stopMode;
    private bool _started;
    private bool _finishing;

    public InstanceOperation(
        InstanceActivity seed,
        ActivityPublisher publisher,
        Action<InstanceOperation> release,
        ILogger logger)
    {
        Key = seed.Key;
        _current = seed;
        _publisher = publisher;
        _release = release;
        _logger = logger;
        Token = _source.Token;

        // NOTE: OnCompleted consumers may start another operation, so expose it only after occupancy is released.
        Stream = publisher.Observe(_subject.Concat(Observable.FromAsync(() => Completion).IgnoreElements()));
    }

    public string Key { get; }
    public CancellationToken Token { get; }
    public Task<InstanceActivity> Completion => _completion.Task;
    public IObservable<InstanceActivity> Stream { get; }

    public InstanceActivity Current
    {
        get
        {
            lock (_gate)
            {
                return _current;
            }
        }
    }

    public bool PreserveProcess
    {
        get
        {
            lock (_gate)
            {
                return _stopMode is StopMode.PreserveProcess;
            }
        }
    }

    public void Start(Func<InstanceOperation, Task> handler)
    {
        lock (_gate)
        {
            if (_started)
            {
                throw new InvalidOperationException("The operation has already started");
            }

            _started = true;
            Enqueue(_current);
        }

        _publisher.Drain();
        _ = ExecuteAsync(handler);
    }

    public void Mutate<T>(Func<T, T> mutator) where T : InstanceActivity
    {
        lock (_gate)
        {
            if (_finishing || _current is not T current)
            {
                return;
            }

            _current = mutator(current);
            Enqueue(_current);
        }

        _publisher.Drain();
    }

    public void Report(ActivityProgress progress) => Mutate<InstanceActivity>(x => x with { Progress = progress });

    public void BeginPhase(InstanceActivity seed)
    {
        lock (_gate)
        {
            Token.ThrowIfCancellationRequested();
            if (_finishing || seed.Key != Key)
            {
                throw new InvalidOperationException("The phase does not belong to an active operation");
            }

            Enqueue(_current with { State = ActivityState.Finished, CompletedAt = DateTimeOffset.Now });
            _current = seed;
            Enqueue(seed);
        }

        _publisher.Drain();
    }

    public void RequestStop(StopMode mode)
    {
        lock (_gate)
        {
            if (_finishing || _stopMode is not null)
            {
                return;
            }

            _stopMode = mode;
            // NOTE: CancelAsync marks the token immediately without invoking callbacks under this lock.
            _cancellation = _source.CancelAsync();
        }
    }

    public T StartProcess<T>(Func<T> start)
    {
        lock (_gate)
        {
            Token.ThrowIfCancellationRequested();
            return start();
        }
    }

    private async Task ExecuteAsync(Func<InstanceOperation, Task> handler)
    {
        Exception? failure = null;
        try
        {
            Token.ThrowIfCancellationRequested();
            await handler(this).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (Token.IsCancellationRequested)
        {
        }
        catch (Exception ex)
        {
            failure = ex;
            _logger.LogError(ex, "Activity {kind} of {key} faulted", Current.Kind, Key);
        }

        Task cancellation;
        lock (_gate)
        {
            _finishing = true;
            cancellation = _cancellation;
        }

        try
        {
            await cancellation.ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Cancellation callbacks of {key} failed", Key);
        }

        _source.Dispose();

        lock (_gate)
        {
            _current = _current with
            {
                State = failure is not null ? ActivityState.Faulted
                    : Token.IsCancellationRequested ? ActivityState.Cancelled : ActivityState.Finished,
                FailureReason = failure,
                CompletedAt = DateTimeOffset.Now
            };
            var final = _current;
            _publisher.Enqueue(() =>
            {
                try
                {
                    Publish(final);
                }
                finally
                {
                    _subject.OnCompleted();
                    _release(this);
                    _completion.TrySetResult(final);
                }
            });
        }

        _publisher.Drain();
    }

    private void Enqueue(InstanceActivity activity) => _publisher.Enqueue(() => Publish(activity));

    private void Publish(InstanceActivity activity)
    {
        _publisher.Publish(activity);
        _subject.OnNext(activity);
    }

    internal enum StopMode { Abort, PreserveProcess }
}
