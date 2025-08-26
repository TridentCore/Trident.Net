using System.Text.Json.Serialization;

namespace Trident.Core.Models.MinecraftApi
{
    public readonly record struct AcquireAccessTokenByXboxServiceTokenRequest(
        [property: JsonPropertyName("identityToken")]
        string IdentityToken);
}
