using TridentCore.Abstractions.FileModels;
using TridentCore.Abstractions.Utilities;
using TridentCore.Core.Services.Instances;

namespace TridentCore.Core.Utilities;

// NOTE: 源叠加优先级（整合包 Source + SourceOrders）的指纹。与 OptionsHash 分离，
//  使层重排只失效 FastMode 门（Verify），不误报 options 变化而触发 SyncPackages 的 floating 重解析。
public static class ViabilityHashHelper
{
    public static string PriorityOf(Profile.Rice setup) =>
        HashHelper.ComputeObjectHash(new { setup.Source, Order = string.Join('\n', setup.SourceOrders) });

    public static string OptionsOf(DeployOptions options) => HashHelper.ComputeObjectHash(options);
}
