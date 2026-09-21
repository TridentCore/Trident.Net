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
        var plan = differ.Diff(Context.Key, target, token);
        var assets = await indexes.EnsureAssetAsync(Context.Lock.Artifact!.AssetIndex, token).ConfigureAwait(false);
        Append(new AssetPlanner().Plan(assets, token));
        if (Context.Lock.RuntimeMajor is { } major)
        {
            var runtime = await indexes.EnsureRuntimeAsync(major, Context.Lock.RuntimeIndex, token).ConfigureAwait(false);
            Append(new RuntimePlanner().Plan(runtime, token));
        }
        Context.Plan = plan;
        logger.LogInformation("Planned {Downloads} downloads and {Operations} local file operations", plan.Downloads.Count, plan.Operations.Count);

        void Append(DeploymentPlan additional)
        {
            plan.Downloads.AddRange(additional.Downloads);
            plan.Operations.AddRange(additional.Operations);
        }
    }
}
