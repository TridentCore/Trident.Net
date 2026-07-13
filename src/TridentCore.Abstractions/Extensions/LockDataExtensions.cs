using TridentCore.Abstractions.FileModels;

namespace TridentCore.Abstractions.Extensions;

public static class LockDataExtensions
{
    // FastMode gate: the on-disk lock is reusable only when the platform, the deploy-options
    // fingerprint, the overlay-priority fingerprint, and the declared (enabled) package set all
    // still match — by full pref (vid included), so a repinned fixed version re-enters the
    // pipeline for SyncPackages to honor.
    public static bool Verify(this LockData self, Profile.Rice setup, string optionsHash, string priorityHash)
    {
        if (self.Platform.Minecraft != setup.Version || self.Platform.Loader != setup.Loader)
        {
            return false;
        }

        if (self.Viability.OptionsHash != optionsHash)
        {
            return false;
        }

        if (self.Viability.PriorityHash != priorityHash)
        {
            return false;
        }

        var setupPrefs = setup.Packages.Where(x => x.Enabled).Select(x => x.Pref).ToHashSet();
        var lockPrefs = self.Packages.Select(x => x.Pref).ToHashSet();
        return setupPrefs.SetEquals(lockPrefs);
    }
}
