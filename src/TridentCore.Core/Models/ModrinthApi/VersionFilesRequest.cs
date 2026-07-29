namespace TridentCore.Core.Models.ModrinthApi;

public record VersionFilesRequest(IReadOnlyList<string> Hashes, string Algorithm = "sha1");
