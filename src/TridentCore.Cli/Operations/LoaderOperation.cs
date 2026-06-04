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
            throw new CliException($"Loader '{loaderId}' is not supported.", ExitCodes.USAGE);
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

    public static LoaderGetResult Get(
        InstanceContextResolver resolver,
        string instance,
        string? profile)
    {
        var ctx = resolver.Resolve(instance, profile);
        var lurl = ctx.Profile.Setup.Loader;
        var parsed =
            !string.IsNullOrWhiteSpace(lurl) && LoaderHelper.TryParse(lurl, out var result)
                ? new LoaderState(lurl, result.Identity, result.Version, LoaderHelper.ToDisplayName(result.Identity), LoaderSupport.IsSupported(result.Identity))
                : new LoaderState(lurl, null, null, null, false);
        return new(ctx.Key, parsed);
    }

    public static LoaderSetResult Set(
        InstanceContextResolver resolver,
        ProfileManager profileManager,
        string loader,
        string instance,
        string? profile)
    {
        if (!LoaderHelper.TryParse(loader, out var parsed))
        {
            throw new CliException($"Loader '{loader}' is not a valid lurl. Use <loader-id>:<version>.", ExitCodes.USAGE);
        }

        if (!LoaderSupport.IsSupported(parsed.Identity))
        {
            throw new CliException($"Loader '{parsed.Identity}' is not supported.", ExitCodes.USAGE);
        }

        var ctx = resolver.Resolve(instance, profile);
        var guard = profileManager.GetMutable(ctx.Key);
        var oldLoader = guard.Value.Setup.Loader;
        guard.Value.Setup.Loader = loader;
        guard.DisposeAsync().AsTask().GetAwaiter().GetResult();
        return new(ctx.Key, oldLoader, loader, parsed.Identity, parsed.Version);
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

internal sealed record LoaderState(string? Lurl, string? Identity, string? Version, string? Name, bool Supported);
internal sealed record LoaderGetResult(string Key, LoaderState Loader);
internal sealed record LoaderSetResult(string Key, string? OldLoader, string Loader, string Identity, string? Version);
