using TridentCore.Abstractions.FileModels;
using TridentCore.Abstractions.Utilities;
using TridentCore.Core.Clients;
using TridentCore.Core.Extensions;
using FileHash = TridentCore.Abstractions.Utilities.FileHash;

namespace TridentCore.Core.Services;

public class AuthlibInjectorService(IAuthlibInjectorClient client)
{
    public record AuthlibInjectorArtifact(string Version, Uri DownloadUrl, FileHash Hash);

    public const string ENDPOINT = "https://authlib-injector.yushi.moe";
    public const string LIBRARY_NAMESPACE = "moe.yushi";
    public const string LIBRARY_NAME = "authlib-injector";

    public static LockData.Library.Identity LibraryIdentity(string version) =>
        new(LIBRARY_NAMESPACE, LIBRARY_NAME, version, null, "jar");

    public async Task<AuthlibInjectorArtifact> GetLatestAsync(CancellationToken token = default)
    {
        var artifact = await client.GetLatestArtifactAsync().ConfigureAwait(false);
        return new(artifact.Version, artifact.DownloadUrl, FileHash.Sha256(artifact.Checksums.Sha256));
    }
}
