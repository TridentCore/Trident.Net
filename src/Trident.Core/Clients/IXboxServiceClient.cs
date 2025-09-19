using Refit;
using Trident.Core.Models.XboxLiveApi;

namespace Trident.Core.Clients;

public interface IXboxServiceClient
{
    [Post("/xsts/authorize")]
    Task<XboxLiveResponse> AcquireMinecraftTokenAsync([Body] XboxLiveRequest<MinecraftTokenProperties> request);
}
