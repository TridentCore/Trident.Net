using TridentCore.Abstractions;
using TridentCore.Abstractions.Utilities;

namespace TridentCore.Core.Engines.Deploying;

public sealed class AssetPlanner
{
    public IEnumerable<DeploymentFileRequirement> Plan(AssetIndex index, CancellationToken token = default)
    {
        var files = new Dictionary<string, DeploymentFileRequirement>(StringComparer.OrdinalIgnoreCase);
        foreach (var asset in index.Objects.Values)
        {
            token.ThrowIfCancellationRequested();
            if (!files.TryGetValue(asset.Hash, out var file))
            {
                var hash = asset.Hash;
                file = new(PathDef.Default.FileOfAssetObject(hash),
                    new Uri($"https://resources.download.minecraft.net/{hash[..2]}/{hash}"), FileHash.Sha1(hash), false);
                files.Add(hash, file);
            }
            yield return file;
        }
    }
}
