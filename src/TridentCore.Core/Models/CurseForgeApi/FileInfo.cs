namespace TridentCore.Core.Models.CurseForgeApi;

public record FileInfo(
    uint Id,
    uint GameId,
    uint ModId,
    bool IsAvailable,
    string DisplayName,
    string FileName,
    FileInfo.FileReleaseType ReleaseType,
    FileInfo.FileStatusStatus FileStatus,
    IReadOnlyList<FileInfo.FileHash> Hashes,
    DateTimeOffset FileDate,
    ulong FileLength,
    ulong DownloadCount,
    ulong? FileSizeOnDisk,
    Uri? DownloadUrl,
    IReadOnlyList<string> GameVersions,
    IReadOnlyList<SortableGameVersionModel> SortableGameVersions,
    IReadOnlyList<FileInfo.FileDependency> Dependencies,
    bool? ExposeAsAlternative,
    uint? ParentProjectFileId,
    uint? AlternativeFileId,
    bool? IsServerPack,
    uint? ServerPackFileId,
    bool? IsEarlyAccessContent,
    DateTimeOffset? EarlyAccessEndDate,
    ulong FileFingerprint,
    IReadOnlyList<FileInfo.FileModule> Modules
)
{
    #region FileReleaseType enum

    public enum FileReleaseType
    {
        RELEASE = 1,
        BETA,
        ALPHA,
    }

    #endregion

    #region FileStatusStatus enum

    public enum FileStatusStatus
    {
        PROCESSING = 1,
        CHANGES_REQUIRED,
        UNDER_REVIEW,
        APPROVED,
        REJECTED,
        MALWARE_DETECTED,
        DELETED,
        ARCHIVED,
        TESTING,
        RELEASED,
        READY_FOR_REVIEW,
        DEPRECATED,
        BAKING,
        AWAITING_PUBLISHING,
        FAILED_PUBLISHING,
    }

    #endregion

    #region Nested type: FileDependency

    public record FileDependency(uint ModId, FileDependency.FileRelationType RelationType)
    {
        #region FileRelationType enum

        public enum FileRelationType
        {
            EMBEDDED_LIBRARY = 1,
            OPTIONAL_DEPENDENCY,
            REQUIRED_DEPENDENCY,
            TOOL,
            INCOMPATIBLE,
            INCLUDE,
        }

        #endregion
    }

    #endregion

    #region Nested type: FileHash

    public record FileHash(string Value, FileHash.HashAlgo Algo)
    {
        #region HashAlgo enum

        public enum HashAlgo
        {
            SHA1 = 1,
            MD5,
        }

        #endregion
    }

    #endregion

    #region Nested type: FileModule

    public record FileModule(string Name, ulong Fingerprint);

    #endregion
}
