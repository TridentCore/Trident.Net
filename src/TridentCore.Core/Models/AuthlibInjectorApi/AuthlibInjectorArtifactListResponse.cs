using System.Text.Json.Serialization;

namespace TridentCore.Core.Models.AuthlibInjectorApi;

public record AuthlibInjectorArtifactListResponse(
    [property: JsonPropertyName("latest_build_number")]
    int LatestBuildNumber,
    [property: JsonPropertyName("artifacts")]
    IReadOnlyList<AuthlibInjectorArtifactEntry> Artifacts);

public record AuthlibInjectorArtifactEntry(
    [property: JsonPropertyName("build_number")]
    int BuildNumber,
    [property: JsonPropertyName("version")]
    string Version);
