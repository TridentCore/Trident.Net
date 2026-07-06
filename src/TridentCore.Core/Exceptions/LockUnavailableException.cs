namespace TridentCore.Core.Exceptions;

public class LockUnavailableException(string key, string lockPath, bool exist)
    : Exception($"Lock of {key} is not available, maybe not built or outdated")
{
    public string Key { get; } = key;
    public string LockPath { get; } = lockPath;
    public bool Exist { get; } = exist;
}
