using Trident.Abstractions.FileModels;

namespace Trident.Core.Services.Profiles;

public class ProfileGuard : IAsyncDisposable
{
    private readonly ProfileHandle _handle;
    private readonly ProfileManager _root;

    internal ProfileGuard(ProfileManager root, ProfileHandle handle)
    {
        _root = root;
        _handle = handle;
    }

    public string Key => _handle.Key;
    public Profile Value => _handle.Value;

    public void NotifyChanged() => _root.OnProfileUpdated(Key, _handle.Value);

    #region IAsyncDisposable Members

    public async ValueTask DisposeAsync()
    {
        await _handle.SaveAsync().ConfigureAwait(false);
        NotifyChanged();
    }

    #endregion

    public void Discard() => _handle.IsActive = false;
}
