using System.Reactive.Disposables;
using TridentCore.Abstractions.Reactive;

namespace TridentCore.Core.Engines.Deploying.Stages;

public abstract class StageBase : IDisposableLifetime
{
    public DeployContext Context { get; set; } = null!;

    protected abstract Task OnProcessAsync(CancellationToken token);

    public Task ProcessAsync(CancellationToken token) => OnProcessAsync(token);

    #region IDisposableLifetime Members

    public CompositeDisposable DisposableLifetime { get; } = new();

    public virtual void Dispose() => DisposableLifetime.Dispose();

    #endregion
}
