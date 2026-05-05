namespace TridentCore.Abstractions.Snapshots;

public interface ISnapshotStoreFactory
{
    ISnapshotStore Open(string key);
}
