using Trident.Core.Clients;
using Trident.Core.Models.MojangLauncherApi;

namespace Trident.Core.Services;

public class MojangService(IMojangLauncherClient launcherClient, IMojangPistonClient pistonClient)
{
    public const string LAUNCHER_ENDPOINT = "https://launchercontent.mojang.com";
    public const string PISTON_ENDPOINT = "https://piston-meta.mojang.com";

    public async Task<MinecraftReleasePatchesResponse> GetMinecraftNewsAsync() =>
        await launcherClient.GetReleasePatchesAsync().ConfigureAwait(false);

    public Uri GetAbsoluteImageUrl(Uri imageUrl) => new(new(LAUNCHER_ENDPOINT, UriKind.Absolute), imageUrl);

    public async Task<IReadOnlyDictionary<string, IDictionary<string, IReadOnlyList<RuntimeEntry>>>>
        GetRuntimeManifestAsync() =>
        await pistonClient.GetRuntimeManifestAsync().ConfigureAwait(false);
}
