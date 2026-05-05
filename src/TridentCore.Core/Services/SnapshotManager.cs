using System.Diagnostics.CodeAnalysis;
using TridentCore.Abstractions.Snapshots;

namespace TridentCore.Core.Services;

public class SnapshotManager(ISnapshotStoreFactory factory, ProfileManager profileManager)
{
    public InstanceSnapshots Open(string key) => new(this, key, factory.Open(key));

    public (SnapshotInfo Snapshot, IReadOnlyList<ReferenceInfo> References) Take(string key)
    {
        // TODO: 扫描文件得到集合

        throw new NotImplementedException();
    }

    #region Nested type: InstanceSnapshots

    public class InstanceSnapshots(SnapshotManager manager, string key, ISnapshotStore store) : IDisposable
    {
        public void Dispose() => store.Dispose();

        public (SnapshotInfo Snapshot, IReadOnlyList<ReferenceInfo> References) Take(string key) => manager.Take(key);

        public SnapshotInfo? GetSnapshot(object id) => store.GetSnapshot(id);

        public bool TryGetSnapshot(object id, [MaybeNullWhen(false)] out SnapshotInfo snapshot)
        {
            snapshot = GetSnapshot(id);
            return snapshot != null;
        }
    }

    #endregion
}
