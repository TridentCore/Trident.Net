using TridentCore.Abstractions.FileModels;
using TridentCore.Core.Utilities;

namespace TridentCore.Core.Extensions;

public static class LockedPackageExtensions
{
    // NOTE: 由锁定包的冻结规则 + 解析结果推导的 build 内相对目标。集中于此，
    //  FlattenPackages（冲突分组）与 GenerateManifest（物化）不会算出不同值。
    public static string RelativeTarget(this LockData.LockedPackage self) =>
        PackagePathHelper.RelativeTarget(self.Rule.Normalizing,
                                         self.Rule.Destination,
                                         self.Resolved.ProjectName,
                                         self.Resolved.FileName,
                                         self.Resolved.Kind);
}
