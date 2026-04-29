using Trident.Abstractions.Utilities;
using Trident.Core.Services;

namespace Trident.Cli.Commands.Loader;

internal static class LoaderSupport
{
    public static IReadOnlyList<LoaderInfo> Supported { get; } =
    [
        .. PrismLauncherService.UidMappings.Select(x =>
            new LoaderInfo(x.Key, LoaderHelper.ToDisplayName(x.Key), x.Value)
        ),
    ];

    public static bool IsSupported(string identity) =>
        PrismLauncherService.UidMappings.ContainsKey(identity);

    public static string GetUid(string identity) =>
        PrismLauncherService.UidMappings.TryGetValue(identity, out var uid)
            ? uid
            : throw new ArgumentException($"Loader '{identity}' is not supported.", nameof(identity));
}

internal sealed record LoaderInfo(string Identity, string Name, string PrismUid);
