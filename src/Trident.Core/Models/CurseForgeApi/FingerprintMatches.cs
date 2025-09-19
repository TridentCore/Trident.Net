namespace Trident.Core.Models.CurseForgeApi;

public record FingerprintMatches(
    bool IsCacheBuilt,
    IReadOnlyList<FingerprintMatches.FingerprintMatch> ExactMatches,
    IReadOnlyList<int> ExactFingerprints,
    IReadOnlyList<FingerprintMatches.FingerprintMatch> PartialMatches,
    IReadOnlyList<int> InstalledFingerprints,
    IReadOnlyList<int> UnmatchedFingerprints)
{
    public record FingerprintMatch(uint Id, FileInfo File, IReadOnlyList<FileInfo> LatestFiles);
}
