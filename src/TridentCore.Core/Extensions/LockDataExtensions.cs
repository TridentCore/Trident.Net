using TridentCore.Abstractions;
using TridentCore.Abstractions.FileModels;
using TridentCore.Abstractions.Utilities;
using TridentCore.Core.Igniters;
using TridentCore.Core.Utilities;

namespace TridentCore.Core.Extensions;

public static class LockDataExtensions
{
    public static Igniter MakeIgniter(this LaunchPlanResult self)
    {
        var igniter = new Igniter();

        foreach (var argument in self.GameArguments)
        {
            igniter.AddGameArgument(argument);
        }

        foreach (var argument in self.JavaArguments)
        {
            igniter.AddJvmArgument(argument);
        }

        foreach (var library in self.Libraries.Where(x => x.IsPresent))
        {
            igniter.AddLibrary(PathDef.Default.FileOfLibrary(library.Id.Namespace,
                                                             library.Id.Name,
                                                             library.Id.Version,
                                                             library.Id.Platform,
                                                             library.Id.Extension));
        }

        igniter.SetMainClass(self.MainClass).SetAssetIndex(self.AssetIndex.Id);

        return igniter;
    }

    // NOTE: 由锁定包的冻结规则 + 解析结果推导的 build 内相对目标。集中于此，
    //  FlattenPackages（冲突分组）与 GenerateManifest（物化）不会算出不同值。
    public static string RelativeTarget(this LockData.LockedPackage self) =>
        PackagePathHelper.RelativeTarget(self.Rule.Normalizing,
                                         self.Rule.Destination,
                                         self.Resolved.ProjectName,
                                         self.Resolved.FileName,
                                         self.Resolved.Kind);

}
