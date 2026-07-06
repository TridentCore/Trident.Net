using TridentCore.Abstractions.FileModels;
using TridentCore.Abstractions.Utilities;

namespace TridentCore.Abstractions.Extensions;

public static class LockDataExtensions
{
    // FastMode gate: the on-disk lock is reusable only when the platform, the deploy-options
    // fingerprint, and the declared (enabled) package set all still match — by full purl (vid
    // included), so a repinned fixed version re-enters the pipeline for SyncPackages to honor.
    public static bool Verify(this LockData self, Profile.Rice setup, string optionsHash)
    {
        if (self.Platform.Minecraft != setup.Version || self.Platform.Loader != setup.Loader)
        {
            return false;
        }

        if (self.Viability.OptionsHash != optionsHash)
        {
            return false;
        }

        var setupPurls = setup.Packages.Where(x => x.Enabled).Select(x => x.Purl).ToHashSet();
        var lockPurls = self.Packages.Select(x => x.Purl).ToHashSet();
        return setupPurls.SetEquals(lockPurls);
    }
}
