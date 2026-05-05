namespace TridentCore.Abstractions.Snapshots;

public interface ISnapshotStore : IDisposable
{
    void Save(SnapshotInfo snapshot, IEnumerable<ReferenceInfo> references);

    IReadOnlyList<SnapshotInfo> GetSnapshots();

    SnapshotInfo? GetSnapshot(object id);

    IReadOnlyList<ReferenceInfo> GetReferences(object snapshotId);

    void DeleteSnapshot(object id);

    ISet<string> GetAllReferencedHashes();
}
