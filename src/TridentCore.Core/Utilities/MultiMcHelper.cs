using TridentCore.Abstractions.Utilities;
using TridentCore.Core.Services;

namespace TridentCore.Core.Utilities;

public static class MultiMcHelper
{
    public const string LABEL = "multimc";
    public const string PACK_INDEX_FILE_NAME = "mmc-pack.json";
    public const string PACK_INSTANCE_CFG = "instance.cfg";
    public const string PACK_MINECRAFT_DIR = ".minecraft";
    public const string UID_MINECRAFT = "net.minecraft";
    public const string UID_LWJGL3 = "org.lwjgl3";

    public static readonly IReadOnlyDictionary<string, string> LoaderToUidMappings = new Dictionary<
        string, string
    >
    {
        [LoaderHelper.LOADERID_FORGE] = PrismLauncherService.UID_FORGE,
        [LoaderHelper.LOADERID_NEOFORGE] = PrismLauncherService.UID_NEOFORGE,
        [LoaderHelper.LOADERID_FABRIC] = PrismLauncherService.UID_FABRIC,
        [LoaderHelper.LOADERID_QUILT] = PrismLauncherService.UID_QUILT,
    };

    public static readonly IReadOnlyDictionary<string, string> UidToLoaderMappings = new Dictionary<
        string, string
    >
    {
        [PrismLauncherService.UID_FORGE] = LoaderHelper.LOADERID_FORGE,
        [PrismLauncherService.UID_NEOFORGE] = LoaderHelper.LOADERID_NEOFORGE,
        [PrismLauncherService.UID_FABRIC] = LoaderHelper.LOADERID_FABRIC,
        [PrismLauncherService.UID_QUILT] = LoaderHelper.LOADERID_QUILT,
    };
}
