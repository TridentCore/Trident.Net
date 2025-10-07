using Trident.Abstractions.Reactive;

namespace Trident.Abstractions.Extensions;

public static class DisposableExtensions
{
    public static IDisposable DisposeWith(this IDisposable self, IDisposableLifetime disposable)
    {
        disposable.DisposableLifetime.Add(self);
        return self;
    }
}
