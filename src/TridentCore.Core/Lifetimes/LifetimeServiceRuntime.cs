using TridentCore.Abstractions.Lifetimes;

namespace TridentCore.Core.Lifetimes;

public sealed class LifetimeServiceRuntime(IEnumerable<ILifetimeService> services)
{
    private readonly ILifetimeService[] _services = services.ToArray();
    private readonly SemaphoreSlim _gate = new(1, 1);

    private int _startedCount;

    public async Task StartAsync(CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            if (_startedCount == _services.Length)
            {
                return;
            }

            var startedCount = 0;
            try
            {
                foreach (var service in _services)
                {
                    await service.StartAsync(cancellationToken);
                    startedCount++;
                }

                _startedCount = startedCount;
            }
            catch
            {
                await StopStartedServicesAsync(startedCount, cancellationToken);
                throw;
            }
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task StopAsync(CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            await StopStartedServicesAsync(_startedCount, cancellationToken);
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task StopStartedServicesAsync(
        int startedCount,
        CancellationToken cancellationToken
    )
    {
        var exceptions = new List<Exception>();
        for (var i = startedCount - 1; i >= 0; i--)
        {
            try
            {
                await _services[i].StopAsync(cancellationToken);
            }
            catch (Exception ex)
            {
                exceptions.Add(ex);
            }
        }

        _startedCount = 0;

        if (exceptions.Count == 1)
        {
            throw exceptions[0];
        }

        if (exceptions.Count > 1)
        {
            throw new AggregateException(
                "One or more lifetime services failed to stop.",
                exceptions
            );
        }
    }
}
