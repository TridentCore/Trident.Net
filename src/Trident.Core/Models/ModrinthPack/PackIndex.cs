namespace Trident.Core.Models.ModrinthPack;

public record PackIndex(
    int FormatVersion,
    string Game,
    string VersionId,
    string Name,
    string? Summary,
    IReadOnlyList<PackIndex.IndexFile> Files,
    IDictionary<string, string> Dependencies)
{
    #region Nested type: IndexFile

    public record IndexFile(
        string Path,
        IndexFile.FileHashes Hashes,
        IndexFile.FileEnv? Env,
        IReadOnlyList<Uri> Downloads,
        ulong FileSize)
    {
        #region Nested type: FileEnv

        public record FileEnv(string Client, string Server);

        #endregion

        #region Nested type: FileHashes

        public record FileHashes(string? Sha1, string? Sha512);

        #endregion
    }

    #endregion
}
