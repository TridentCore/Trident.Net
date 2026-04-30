using System.Text.Json.Serialization;

namespace TridentCore.Core.Models.MinecraftApi;

public record AcquireAccessTokenByXboxServiceTokenRequest(
    [property: JsonPropertyName("identityToken")] string IdentityToken
);
