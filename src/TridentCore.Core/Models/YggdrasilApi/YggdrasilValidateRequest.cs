using System.Text.Json.Serialization;

namespace TridentCore.Core.Models.YggdrasilApi;

public record YggdrasilValidateRequest(
    [property: JsonPropertyName("accessToken")] string AccessToken,
    [property: JsonPropertyName("clientToken")] string? ClientToken
);
