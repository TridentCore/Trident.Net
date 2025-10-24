using Refit;
using Trident.Core.Models.MojangLauncherApi;

namespace Trident.Core.Clients;

public interface IMojangLauncherClient
{
    [Get("/v2/javaPatchNotes.json")]
    Task<MinecraftReleasePatchesResponse> GetReleasePatchesAsync();
}
