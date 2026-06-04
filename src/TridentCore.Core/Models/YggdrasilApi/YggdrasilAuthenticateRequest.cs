using System.Text.Json.Serialization;

namespace TridentCore.Core.Models.YggdrasilApi;

public record YggdrasilAuthenticateRequest(
    [property: JsonPropertyName("agent")] YggdrasilAgent? Agent,
    [property: JsonPropertyName("username")] string Username,
    [property: JsonPropertyName("password")] string Password,
    [property: JsonPropertyName("clientToken")] string? ClientToken,
    [property: JsonPropertyName("requestUser")] bool RequestUser
);

public record YggdrasilAgent(
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("version")] int Version
);
