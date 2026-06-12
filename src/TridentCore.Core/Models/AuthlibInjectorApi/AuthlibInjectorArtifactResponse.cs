using System.Text.Json.Serialization;

namespace TridentCore.Core.Models.AuthlibInjectorApi;

public record AuthlibInjectorArtifactResponse(
    [property: JsonPropertyName("build_number")]
    int BuildNumber,
    [property: JsonPropertyName("version")]
    string Version,
    [property: JsonPropertyName("download_url")]
    Uri DownloadUrl,
    [property: JsonPropertyName("checksums")]
    AuthlibInjectorArtifactResponse.AuthlibInjectorChecksums Checksums)
{


    public record AuthlibInjectorChecksums(
        [property: JsonPropertyName("sha256")] string Sha256
    );

}
