using Refit;
using TridentCore.Core.Models.XboxLiveApi;

namespace TridentCore.Core.Clients;

public interface IXboxLiveClient
{
    [Post("/user/authenticate")]
    Task<XboxLiveResponse> AcquireXboxLiveTokenAsync([Body] XboxLiveAuthenticateRequest request);
}
