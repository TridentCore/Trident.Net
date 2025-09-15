using System.Text.Json.Serialization;

namespace Trident.Core.Models.MinecraftApi;

public record AcquireAccessTokenByXboxServiceTokenRequest(
    [property: JsonPropertyName("identityToken")]
    string IdentityToken);
