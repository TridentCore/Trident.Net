using TridentCore.Abstractions.FileModels;

namespace TridentCore.Abstractions.Extensions;

public static class LockDataExtensions
{
    // NOTE: FastMode 门——仅当 platform、deploy-options 指纹、叠加优先级指纹与声明（启用）包集合
    //  全部匹配时磁盘锁才可复用；按完整 pref（含 vid）比较，重钉的固定版本因此重新进入
    //  管线交由 SyncPackages 处理。
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
