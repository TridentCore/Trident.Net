using Refit;
using TridentCore.Core.Models.YggdrasilApi;

namespace TridentCore.Core.Clients;

public interface IYggdrasilClient
{
    [Post("/authserver/authenticate")]
    Task<YggdrasilAuthenticateResponse> AuthenticateAsync(
        [Body] YggdrasilAuthenticateRequest request,
        CancellationToken token = default
    );

    [Post("/authserver/refresh")]
    Task<YggdrasilAuthenticateResponse> RefreshAsync(
        [Body] YggdrasilRefreshRequest request,
        CancellationToken token = default
    );

    [Post("/authserver/validate")]
    Task ValidateAsync(
        [Body] YggdrasilValidateRequest request,
        CancellationToken token = default
    );
}
