using Trident.Core.Models.MojangLauncherApi;
using Refit;

namespace Trident.Core.Clients
{
    public interface IMojangLauncherClient
    {
        [Get("/news.json")]
        Task<MinecraftNewsResponse> GetNewsAsync();
    }
}
