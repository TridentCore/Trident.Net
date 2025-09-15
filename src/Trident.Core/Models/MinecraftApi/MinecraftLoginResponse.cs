namespace Trident.Core.Models.MinecraftApi;

public record MinecraftLoginResponse(
    string? Error,
    string? ErrorMessage,
    string Username,
    string AccessToken,
    string TokenType,
    int ExpiresIn);
