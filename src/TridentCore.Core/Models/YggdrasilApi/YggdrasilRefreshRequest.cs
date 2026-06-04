using System.Text.Json.Serialization;

namespace TridentCore.Core.Models.YggdrasilApi;

public record YggdrasilRefreshRequest(
    [property: JsonPropertyName("accessToken")] string AccessToken,
    [property: JsonPropertyName("clientToken")] string ClientToken,
    [property: JsonPropertyName("requestUser")] bool RequestUser,
    [property: JsonPropertyName("selectedProfile")]
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    YggdrasilGameProfile? SelectedProfile = null
);
