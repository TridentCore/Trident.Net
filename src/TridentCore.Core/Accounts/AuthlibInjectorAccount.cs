using TridentCore.Abstractions.Accounts;

namespace TridentCore.Core.Accounts;

public class AuthlibInjectorAccount : IAccount
{
    public required string ServerUrl { get; init; }

    public required string Password { get; set; }

    public required string AccessToken { get; set; }

    public DateTimeOffset? AccessTokenExpiresAt { get; set; }

    public string? ClientToken { get; set; }

    #region IAccount Members

    public required string Username { get; init; }

    public required string Uuid { get; init; }

    string IAccount.UserType => "mojang";

    #endregion
}
