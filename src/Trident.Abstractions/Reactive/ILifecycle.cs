using System;

namespace Trident.Abstractions.Reactive;

public interface ILifecycle
{
    public void OnInitialize();
    public void OnDeinitialize();
}
