using TridentCore.Abstractions.Accounts;

namespace TridentCore.Core.Accounts;

public class AuthlibInjectorAccount : IAccount
{
    public required string ServerUrl { get; init; }

    public required string AccessToken { get; set; }

    public string? ClientToken { get; set; }

    #region IAccount Members

    public required string Username { get; set; }

    public required string Uuid { get; set; }

    string IAccount.UserType => "mojang";

    #endregion
}
