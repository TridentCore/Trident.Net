using System;

namespace Trident.Abstractions.Reactive;

public interface IAsyncLifecycle
{
    public Task OnInitializeAsync();
    public Task OnDeinitializeAsync();
}
