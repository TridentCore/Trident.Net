using TridentCore.Abstractions.Utilities;
using TridentCore.Cli.Commands.Loader;
using TridentCore.Cli.Services;
using TridentCore.Core.Services;

namespace TridentCore.Cli.Operations;

internal static class LoaderOperation
{
    public static IReadOnlyList<LoaderInfo> List() => LoaderSupport.Supported;

    public static async Task<LoaderVersionListResult> VersionList(
        PrismLauncherService prismLauncher,
        string loaderId,
        string version,
        string sort,
        int index,
        int limit)
    {
        if (!LoaderSupport.IsSupported(loaderId))
        {
            throw new CliException($"Loader '{loaderId}' is not supported.", ExitCodes.Usage);
        }

        var uid = LoaderSupport.GetUid(loaderId);
        var versions = await prismLauncher.GetVersionsForMinecraftVersionAsync(uid, version, CancellationToken.None).ConfigureAwait(false);

        var sorted = string.Equals(sort, "asc", StringComparison.OrdinalIgnoreCase)
            ? versions.OrderBy(x => x.ReleaseTime)
            : versions.OrderByDescending(x => x.ReleaseTime);

        var page = sorted.Skip(index).Take(limit).Select(x => new LoaderVersionItem(
            x.Version,
            LoaderHelper.ToLurl(loaderId, x.Version),
            x.Type,
            x.Recommended,
            x.ReleaseTime
        )).ToArray();

        return new(loaderId, version, versions.Count, page);
    }
}

public sealed record LoaderVersionListResult(
    string Loader,
    string GameVersion,
    int Total,
    IReadOnlyList<LoaderVersionItem> Items
);

public sealed record LoaderVersionItem(
    string Version,
    string Lurl,
    string Type,
    bool Recommended,
    DateTimeOffset ReleaseTime
);
