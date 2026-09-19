using TridentCore.Abstractions;
using TridentCore.Abstractions.FileModels;
using TridentCore.Abstractions.Utilities;
using TridentCore.Core.Engines.Deploying;
using TridentCore.Core.Exceptions;
using TridentCore.Core.Utilities;

namespace TridentCore.Core.Services;

public sealed class DeploymentIndexService(MojangService mojang, IHttpClientFactory factory)
{
    public async Task<AssetIndex> EnsureAssetAsync(LockData.AssetData reference, CancellationToken token)
    {
        if (await DeploymentIndexHelper.ReadAssetAsync(reference, token).ConfigureAwait(false) is { } cached) return cached;
        using var client = factory.CreateClient(RepositoryAgent.CLIENT_NAME);
        await DownloadHelper.DownloadAsync(client, reference.Url, PathDef.Default.FileOfAssetIndex(reference.Id), reference.Hash, token).ConfigureAwait(false);
        return await DeploymentIndexHelper.ReadAssetAsync(reference, token).ConfigureAwait(false)
            ?? throw new InvalidDataException("Downloaded asset index is invalid.");
    }

    public async Task<RuntimeIndex> EnsureRuntimeAsync(uint major, CancellationToken token)
    {
        if (await DeploymentIndexHelper.ReadRuntimeAsync(major, token).ConfigureAwait(false) is { } cached) return cached;
        var catalog = await mojang.GetRuntimeManifestAsync().ConfigureAwait(false);
        var platform = JavaHelper.RuntimePlatform();
        var family = JavaHelper.BundledFamily(major);
        if (platform is null || family is null || !catalog.TryGetValue(platform, out var runtimes)
            || !runtimes.TryGetValue(family, out var candidates)) throw new JavaNotFoundException(major);
        var selected = candidates.Where(x => JavaHelper.ParseJavaMajor(x.Version.Name) == major)
            .OrderByDescending(x => x.Version.Released).FirstOrDefault() ?? throw new JavaNotFoundException(major);
        using var client = factory.CreateClient(RepositoryAgent.CLIENT_NAME);
        var path = PathDef.Default.FileOfRuntimeManifest(major);
        await DownloadHelper.DownloadAsync(client, selected.Manifest.Url, path, FileHash.Sha1(selected.Manifest.Sha1), token).ConfigureAwait(false);
        return await DeploymentIndexHelper.ReadRuntimeAsync(major, token).ConfigureAwait(false)
            ?? throw new InvalidDataException("Downloaded runtime index is invalid.");
    }
}
