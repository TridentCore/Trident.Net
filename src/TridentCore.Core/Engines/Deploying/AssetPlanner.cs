using TridentCore.Abstractions;
using TridentCore.Abstractions.Utilities;
using TridentCore.Core.Utilities;

namespace TridentCore.Core.Engines.Deploying;

public sealed class AssetPlanner
{
    public void Plan(DeploymentTarget target, AssetIndex index, CancellationToken token = default)
    {
        foreach (var hash in index.Objects.Values.Select(x => x.Hash).Distinct(StringComparer.OrdinalIgnoreCase))
        {
            token.ThrowIfCancellationRequested();
            DeploymentFileHelper.RequireFile(target, PathDef.Default.FileOfAssetObject(hash),
                new Uri($"https://resources.download.minecraft.net/{hash[..2]}/{hash}"), FileHash.Sha1(hash));
        }
    }
}
