using Refit;
using Trident.Core.Models.MinecraftApi;

namespace Trident.Core.Clients;

public interface IMinecraftClient
{
    [Post("/authentication/login_with_xbox")]
    Task<MinecraftLoginResponse> AcquireAccessTokenByXboxServiceTokenAsync(
        [Body] AcquireAccessTokenByXboxServiceTokenRequest request);

    [Get("/entitlements/mcstore")]
    Task<MinecraftStoreResponse> AcquireAccountInventoryByAccessTokenAsync([Authorize] string accessToken);

    [Get("/minecraft/profile")]
    Task<MinecraftProfileResponse> AcquireAccountProfileByMinecraftTokenAsync([Authorize] string accessToken);
}
