using TridentCore.Abstractions.Utilities;
using TridentCore.Core.Clients;
using TridentCore.Core.Models.PrismLauncherApi;

namespace TridentCore.Core.Services;

public class PrismLauncherService(IPrismLauncherClient client)
{
    public const string ENDPOINT = "https://meta.prismlauncher.org";
    public const string UID_MINECRAFT = "net.minecraft";
    public const string UID_FORGE = "net.minecraftforge";
    public const string UID_NEOFORGE = "net.neoforged";
    public const string UID_INTERMEDIARY = "net.fabricmc.intermediary";
    public const string UID_FABRIC = "net.fabricmc.fabric-loader";
    public const string UID_QUILT = "org.quiltmc.quilt-loader";

    public static readonly IReadOnlyDictionary<string, string> UidMappings = new Dictionary<string, string>
    {
        [LoaderHelper.LOADERID_FORGE] = UID_FORGE,
        [LoaderHelper.LOADERID_NEOFORGE] = UID_NEOFORGE,
        [LoaderHelper.LOADERID_FABRIC] = UID_FABRIC,
        [LoaderHelper.LOADERID_QUILT] = UID_QUILT
    };

    public Task<ComponentIndex> GetVersionsAsync(string uid, CancellationToken token) => client.GetComponentIndexAsync(uid, token);

    public async Task<IReadOnlyList<ComponentIndex.ComponentVersion>> GetVersionsForMinecraftVersionAsync(
        string uid, string version, CancellationToken token)
    {
        var index = await GetVersionsAsync(uid, token).ConfigureAwait(false);
        return [.. index.Versions.Where(x => x.Requires.Any(y => y.Uid == UID_INTERMEDIARY
            || (y.Uid == UID_MINECRAFT && (y.Suggest == version || y.Equal == version))))];
    }

    public Task<ComponentIndex> GetMinecraftVersionsAsync(CancellationToken token) => GetVersionsAsync(UID_MINECRAFT, token);
    public Task<Component> GetVersionAsync(string uid, string version, CancellationToken token) => client.GetComponentAsync(uid, version, token);
    public Task<RuntimeManifest> GetRuntimeAsync(uint major, CancellationToken token) => client.GetRuntimeAsync(major, token);
}
