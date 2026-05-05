using TridentCore.Abstractions.FileModels;

namespace TridentCore.Abstractions.Snapshots;

public record SnapshotInfo(
    object Id,
    string Label,
    string Remark,
    Profile.Rice Metadata,
    int PackageCount,
    int FileCount,
    long TotalSize,
    DateTime CreatedAt);
