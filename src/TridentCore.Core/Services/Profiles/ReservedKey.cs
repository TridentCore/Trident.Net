namespace TridentCore.Core.Services.Profiles;

public class ReservedKey : IDisposable
{
    private readonly ProfileManager _root;

    internal ReservedKey(string key, ProfileManager root)
    {
        _root = root;
        Key = key;
    }

    public string Key { get; }

    #region IDisposable Members

    public void Dispose() =>
        // NOTE: 存在临界竞态可能，但概率很低。
        _root.ReservedKeys.Remove(this);

    #endregion
}
