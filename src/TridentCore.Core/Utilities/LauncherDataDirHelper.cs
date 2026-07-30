using System.Runtime.InteropServices;

namespace TridentCore.Core.Utilities;

// Resolves the conventional per-user data directories that third-party launchers install under.
// Each launcher stores at a brand-named folder beneath one of these roots; the adapter supplies the
// brand names and this helper supplies the platform-correct root.
public static class LauncherDataDirHelper
{
    // The conventional application-data root for the current platform — AppData\Roaming on Windows,
    // ~/Library/Application Support on macOS, ~/.local/share on Linux. Returns null when the platform
    // has no equivalent the environment can resolve.
    public static string? ConventionalDataRoot() =>
        RuntimeInformation.IsOSPlatform(OSPlatform.OSX)
            ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                           "Library",
                           "Application Support")
            : RuntimeInformation.IsOSPlatform(OSPlatform.Windows)
                ? Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData)
                : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".local", "share");

    // Returns the first existing candidate folder under the conventional data root, or null when none
    // of the candidates are present. Adapters use this to prefill the directory picker.
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
