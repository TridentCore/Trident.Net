using Microsoft.Extensions.Logging;
using TridentCore.Abstractions.LaunchPlans;

namespace TridentCore.Core.Engines.Deploying.Stages;

// 折叠平台层与磁盘上的来源/用户层为最终启动计划。全流程唯一写入 Lock.LaunchPlan 的位置。
public class ResolveLaunchPlanStage(ILogger<ResolveLaunchPlanStage> logger) : StageBase
{
    protected override Task OnProcessAsync(CancellationToken token)
    {
        token.ThrowIfCancellationRequested();

        // 平台层未重建（缓存命中）时以上一次的结果作为基底层，外部层照常叠加在其之上。
        var layers = Context.PlatformLayers.Count > 0
                         ? Context.PlatformLayers
                         : throw new InvalidOperationException("No platform layer produced before resolution");
        var overlays = Context.LaunchPlanSnapshot?.ToLayers() ?? [];
        var folded = LaunchPlanFolder.Fold([.. layers, .. overlays]);

        Context.Lock = Context.Lock with { LaunchPlan = folded.Result };

        foreach (var displaced in folded.Overrides)
        {
            logger.LogWarning("Launch layer {layer} overrode {subject}: {previous} -> {current}",
                              displaced.Layer.Label,
                              displaced.Subject,
                              displaced.Previous,
                              displaced.Current);
        }

        logger.LogInformation("Folded {layers} launch layers into {libraries} libraries and {jvmArguments} JVM arguments",
                              layers.Count + overlays.Count,
                              folded.Result.Libraries.Length,
                              folded.Result.JavaArguments.Length);
        return Task.CompletedTask;
    }
}
