using Refit;
using TridentCore.Core.Models.AuthlibInjectorApi;

namespace TridentCore.Core.Clients;

public interface IAuthlibInjectorClient
{
    [Get("/artifacts.json")]
    Task<AuthlibInjectorArtifactListResponse> GetArtifactsAsync();

    [Get("/artifact/{buildNumber}.json")]
    Task<AuthlibInjectorArtifactResponse> GetArtifactAsync(int buildNumber);

    [Get("/artifact/latest.json")]
    Task<AuthlibInjectorArtifactResponse> GetLatestArtifactAsync();
}
