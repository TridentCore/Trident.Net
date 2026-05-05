namespace TridentCore.Abstractions.Lifetimes;

public interface ILifetimeService
{
    ValueTask StartAsync(CancellationToken cancellationToken = default);

    ValueTask StopAsync(CancellationToken cancellationToken = default);
}
