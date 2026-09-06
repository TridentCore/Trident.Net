using TridentCore.Abstractions;
using TridentCore.Abstractions.Launching;

namespace TridentCore.Core.Utilities;

public static class LaunchPathHelper
{
    public static string AssetDirectory(string key, CompiledLaunch launch) => launch.AssetIndex.Url.IsFile
        ? Path.Combine(PathDef.Default.DirectoryOfBuild(key), "assets")
        : PathDef.Default.CacheAssetDirectory;
}
