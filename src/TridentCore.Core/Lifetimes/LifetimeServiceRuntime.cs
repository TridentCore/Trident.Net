using TridentCore.Abstractions.Lifetimes;

namespace TridentCore.Core.Lifetimes;

public sealed class LifetimeServiceRuntime
{
    private readonly object _sync = new();
    private readonly ILifetimeService[] _services;
    private readonly bool[] _started;
    private bool _stopping;

    public LifetimeServiceRuntime(IEnumerable<ILifetimeService> services)
    {
        _services = services.ToArray();
        _started = new bool[_services.Length];
    }

    public async Task StartAsync(CancellationToken cancellationToken = default)
    {
        for (var i = 0; i < _services.Length; i++)
        {
            bool abort;
            bool skip;
            lock (_sync)
            {
                abort = _stopping;
                skip = _started[i];
            }

            // NOTE: _stopping is a one-way latch. Once shutdown has been requested the runtime
            // will not start any further services, so a slow Start can never block a Stop.
            if (abort)
            {
                return;
            }

            if (skip)
            {
                continue;
            }

            try
            {
                await _services[i].StartAsync(cancellationToken);
            }
            catch
            {
                await StopRangeAsync(i, cancellationToken);
                throw;
            }

            lock (_sync)
            {
                _started[i] = true;
            }
        }
    }

    public async Task StopAsync(CancellationToken cancellationToken = default)
    {
        lock (_sync)
        {
            _stopping = true;
        }

        await StopRangeAsync(_services.Length, cancellationToken);
    }

    private async Task StopRangeAsync(int count, CancellationToken cancellationToken)
    {
        var exceptions = new List<Exception>();
        for (var i = count - 1; i >= 0; i--)
        {
            bool shouldStop;
            lock (_sync)
            {
                shouldStop = _started[i];
                _started[i] = false;
            }

            if (!shouldStop)
            {
                continue;
            }

            try
            {
                await _services[i].StopAsync(cancellationToken);
            }
            catch (Exception ex)
            {
                exceptions.Add(ex);
            }
        }

        if (exceptions.Count == 1)
        {
            throw exceptions[0];
        }

        if (exceptions.Count > 1)
        {
            throw new AggregateException("One or more lifetime services failed to stop.", exceptions);
        }
    }
}
