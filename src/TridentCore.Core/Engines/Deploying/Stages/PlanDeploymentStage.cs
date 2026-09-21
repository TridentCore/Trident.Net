using Microsoft.Extensions.Logging;
using TridentCore.Core.Services;

namespace TridentCore.Core.Engines.Deploying.Stages;

public class PlanDeploymentStage(
    DeploymentPlanner planner,
    DeploymentDiffer differ,
    DeploymentIndexService indexes,
    ILogger<PlanDeploymentStage> logger) : StageBase
{
    protected override async Task OnProcessAsync(CancellationToken token)
    {
        var target = planner.CreateTarget(Context.Key, Context.Lock, token);
        var assets = await indexes.EnsureAssetAsync(Context.Lock.Artifact!.AssetIndex, token).ConfigureAwait(false);
        new AssetPlanner().Plan(target, assets, token);
        if (Context.Lock.RuntimeMajor is { } major)
        {
            var runtime = await indexes.EnsureRuntimeAsync(major, Context.Lock.RuntimeIndex, token).ConfigureAwait(false);
            new RuntimePlanner().Plan(target, runtime, token);
        }
        var plan = differ.Diff(Context.Key, target, token);
        Context.Plan = plan;
        logger.LogInformation("Planned {Downloads} downloads and {Operations} local file operations", plan.Downloads.Count, plan.Operations.Count);
    }
}
