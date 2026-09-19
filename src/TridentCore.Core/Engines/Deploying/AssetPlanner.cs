using TridentCore.Abstractions;
using TridentCore.Abstractions.Utilities;
using TridentCore.Core.Utilities;

namespace TridentCore.Core.Engines.Deploying;

public sealed class AssetPlanner
{
    public DeploymentPlan Plan(AssetIndex index, CancellationToken token = default)
    {
        var plan = new DeploymentPlan();
        foreach (var hash in index.Objects.Values.Select(x => x.Hash).Distinct(StringComparer.OrdinalIgnoreCase))
        {
            token.ThrowIfCancellationRequested();
            FilePlanningHelper.RequireFile(plan, PathDef.Default.FileOfAssetObject(hash),
                new Uri($"https://resources.download.minecraft.net/{hash[..2]}/{hash}"), FileHash.Sha1(hash));
        }
        return plan;
    }
}
