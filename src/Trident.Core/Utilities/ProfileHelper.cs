using Trident.Abstractions;

namespace Trident.Core.Utilities;

public static class ProfileHelper
{
    public static string? PickIcon(string key) =>
        FileHelper.PickExists(PathDef.Default.DirectoryOfHome(key),
                              ["icon.png", "icon.jpeg", "icon.jpg", "icon.webp", "icon.bmp", "icon.gif"]);

    public static string? PickScreenshotRandomly(string key)
    {
        var screenshots = AssetHelper
                         .ScanNonSymlinks(key, "*.png", ["screenshots"])
                         .Where(x => x.Length != 0)
                         .ToArray();
        if (screenshots.Length == 0)
        {
            return null;
        }

        var index = Random.Shared.Next(screenshots.Length);
        return screenshots[index].FullName;
    }
}
