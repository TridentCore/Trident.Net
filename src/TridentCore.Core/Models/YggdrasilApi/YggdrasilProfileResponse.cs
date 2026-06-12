using System.Text.Json.Serialization;

namespace TridentCore.Core.Models.YggdrasilApi;

public record YggdrasilProfileResponse(
    [property: JsonPropertyName("id")] string Id,
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("properties")]
    YggdrasilProfileProperty[]? Properties
);

public record YggdrasilProfileProperty(
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("value")] string Value,
    [property: JsonPropertyName("signature")] string? Signature
);

public record YggdrasilTexturesData(
    [property: JsonPropertyName("textures")]
    Dictionary<string, YggdrasilTextureInfo> Textures
);

public record YggdrasilTextureInfo(
    [property: JsonPropertyName("url")] string Url,
    [property: JsonPropertyName("metadata")]
    Dictionary<string, string>? Metadata
);
