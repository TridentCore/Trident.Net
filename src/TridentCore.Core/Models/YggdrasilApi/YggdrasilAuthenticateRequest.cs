namespace TridentCore.Core.Models.YggdrasilApi;

public record YggdrasilAuthenticateRequest(
    YggdrasilAgent? Agent,
    string Username,
    string Password,
    string? ClientToken,
    bool RequestUser);
