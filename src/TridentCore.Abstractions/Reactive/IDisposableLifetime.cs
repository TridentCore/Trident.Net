using System.Reactive.Disposables;

namespace TridentCore.Abstractions.Reactive;

public interface IDisposableLifetime : IDisposable
{
    CompositeDisposable DisposableLifetime { get; }
}
