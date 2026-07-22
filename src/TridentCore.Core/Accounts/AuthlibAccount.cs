using TridentCore.Abstractions.Accounts;

namespace TridentCore.Core.Accounts;

public class AuthlibAccount : IAccount
{
    public required string ServerUrl { get; init; }

    public string? ClientToken { get; set; }

    public string? SkinUrl { get; set; }

    public required string AccessToken { get; set; }

    #region IAccount Members

    public required string Username { get; init; }

    public required string Uuid { get; init; }

    string IAccount.UserType => "mojang";

    #endregion
}
