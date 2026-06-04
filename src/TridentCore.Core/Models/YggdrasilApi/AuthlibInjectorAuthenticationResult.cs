using TridentCore.Core.Accounts;

namespace TridentCore.Core.Models.YggdrasilApi;

public record AuthlibInjectorAuthenticationResult(
    AuthlibAccount? Account,
    IReadOnlyList<YggdrasilGameProfile>? AvailableProfiles,
    string ServerUrl,
    string AccessToken,
    string ClientToken
);
