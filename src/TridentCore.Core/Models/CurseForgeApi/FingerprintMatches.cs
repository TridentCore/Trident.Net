namespace TridentCore.Core.Models.CurseForgeApi;

public record FingerprintMatches(
    bool IsCacheBuilt,
    IReadOnlyList<FingerprintMatches.FingerprintMatch> ExactMatches,
    IReadOnlyList<uint> ExactFingerprints,
    IReadOnlyList<FingerprintMatches.FingerprintMatch> PartialMatches,
    IReadOnlyList<uint> InstalledFingerprints,
    IReadOnlyList<uint> UnmatchedFingerprints)
{
    #region Nested type: FingerprintMatch

    public record FingerprintMatch(uint Id, FileInfo File, IReadOnlyList<FileInfo> LatestFiles);

    #endregion
}
