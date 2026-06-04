using System.Text.Json.Serialization;

namespace TridentCore.Core.Models.YggdrasilApi;

public record YggdrasilAuthenticateResponse(
    [property: JsonPropertyName("accessToken")] string AccessToken,
    [property: JsonPropertyName("clientToken")] string ClientToken,
    [property: JsonPropertyName("availableProfiles")]
    IReadOnlyList<YggdrasilGameProfile>? AvailableProfiles,
    [property: JsonPropertyName("selectedProfile")]
    YggdrasilGameProfile? SelectedProfile
);

public record YggdrasilGameProfile(
    [property: JsonPropertyName("id")] string Id,
    [property: JsonPropertyName("name")] string Name
);
