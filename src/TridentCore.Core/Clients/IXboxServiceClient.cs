using Refit;
using TridentCore.Core.Models.XboxLiveApi;

namespace TridentCore.Core.Clients;

public interface IXboxServiceClient
{
    [Post("/xsts/authorize")]
    Task<XboxLiveResponse> AcquireMinecraftTokenAsync(
        [Body] XboxLiveRequest<MinecraftTokenProperties> request
    );
}
