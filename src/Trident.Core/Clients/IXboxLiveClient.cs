using Trident.Core.Models.XboxLiveApi;
using Refit;

namespace Trident.Core.Clients;

public interface IXboxLiveClient
{
    [Post("/user/authenticate")]
    Task<XboxLiveResponse> AcquireXboxLiveTokenAsync([Body] XboxLiveRequest<XboxLiveTokenProperties> request);
}