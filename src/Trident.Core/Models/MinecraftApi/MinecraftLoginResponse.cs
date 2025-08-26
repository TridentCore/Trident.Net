namespace Trident.Core.Models.MinecraftApi
{
    public readonly record struct MinecraftLoginResponse(
        string? Error,
        string? ErrorMessage,
        string Username,
        string AccessToken,
        string TokenType,
        int ExpiresIn);
}
