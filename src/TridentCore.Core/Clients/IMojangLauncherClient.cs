using Refit;
using TridentCore.Core.Models.MojangLauncherApi;

namespace TridentCore.Core.Clients;

public interface IMojangLauncherClient
{
    [Get("/v2/news.json")]
    Task<MinecraftNewsResponse> GetNewsAsync();
}
