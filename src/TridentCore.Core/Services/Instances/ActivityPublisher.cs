using System.Reactive.Linq;
using System.Reactive.Subjects;
using Microsoft.Extensions.Logging;

namespace TridentCore.Core.Services.Instances;

internal sealed class ActivityPublisher(ILogger logger)
{
    private readonly Lock _gate = new();
    private readonly Queue<Action> _pending = new();
    private readonly Subject<InstanceActivity> _subject = new();
    private bool _publishing;

    public IObservable<InstanceActivity> Stream => Observe(_subject);

    public IObservable<InstanceActivity> Observe(IObservable<InstanceActivity> source) =>
        Observable.Create<InstanceActivity>(observer => source.Subscribe(
            value => Notify(() => observer.OnNext(value)),
            error => Notify(() => observer.OnError(error)),
            () => Notify(observer.OnCompleted)));

    public void Publish(InstanceActivity activity) => _subject.OnNext(activity);

    // NOTE: Enqueue may run under an operation's state lock; Drain must run outside it.
    public void Enqueue(Action notification)
    {
        lock (_gate)
        {
            _pending.Enqueue(notification);
        }
    }

    public void Drain()
    {
        lock (_gate)
        {
            if (_publishing)
            {
                return;
            }

            _publishing = true;
        }

        try
        {
            while (true)
            {
                Action notification;
                lock (_gate)
                {
                    if (!_pending.TryDequeue(out notification!))
                    {
                        _publishing = false;
                        return;
                    }
                }

                notification();
            }
        }
        catch
        {
            lock (_gate)
            {
                _publishing = false;
            }

            throw;
        }
    }

    private void Notify(Action notification)
    {
        try
        {
            notification();
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "An instance activity observer failed");
        }
    }
}
