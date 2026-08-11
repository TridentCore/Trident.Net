using System.Runtime.InteropServices;

namespace TridentCore.Core.Utilities;

// NOTE: 解析第三方启动器按用户目录约定的安装位置——各启动器存于某根目录下的品牌名文件夹，
//  适配器提供品牌名，本助手提供平台正确的根。
public static class LauncherDataDirHelper
{
    // NOTE: 当前平台约定的应用数据根——Windows AppData\Roaming，macOS ~/Library/Application Support，
    //  Linux ~/.local/share；平台无等价位置时返回 null。
    public static string? ConventionalDataRoot() =>
        RuntimeInformation.IsOSPlatform(OSPlatform.OSX)
            ?
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                         "Library",
                         "Application Support")
            : RuntimeInformation.IsOSPlatform(OSPlatform.Windows)
                ? Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData)
                : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".local", "share");

    // NOTE: 返回约定数据根下第一个存在的候选目录，全无则 null——适配器用它预填目录选择器。
    public static string? LocateUnderConventional(params string[] candidates)
    {
        var root = ConventionalDataRoot();
        if (string.IsNullOrEmpty(root))
        {
            return null;
        }

        foreach (var name in candidates)
        {
            var dir = Path.Combine(root, name);
            if (Directory.Exists(dir))
            {
                return dir;
            }
        }

        return null;
    }
}
