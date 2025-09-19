using Refit;
using Trident.Core.Models.MojangLauncherApi;

namespace Trident.Core.Clients;

public interface IMojangLauncherClient
{
    [Get("/news.json")]
    Task<MinecraftNewsResponse> GetNewsAsync();
}
