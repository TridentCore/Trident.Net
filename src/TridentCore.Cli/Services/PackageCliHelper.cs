using TridentCore.Abstractions.FileModels;
using TridentCore.Abstractions.Repositories;
using TridentCore.Abstractions.Repositories.Resources;
using TridentCore.Abstractions.Utilities;

namespace TridentCore.Cli.Services;

public static class PackageCliHelper
{
    public static (string Label, string? Namespace, string Pid, string? Vid) ParsePurl(
        string purl
    ) =>
        PackageHelper.TryParse(purl, out var parsed)
            ? parsed
            : throw new CliException($"'{purl}' is not a valid package purl.", ExitCodes.Usage);

    public static Filter BuildFilter(
        string? gameVersion,
        string? loader,
        ResourceKind? kind,
        ResolvedInstanceContext? instance = null
    )
    {
        gameVersion ??= instance?.Profile.Setup.Version;
        if (loader is null && instance?.Profile.Setup.Loader is { } lurl)
        {
            loader = LoaderHelper.TryParse(lurl, out var parsed) ? parsed.Identity : null;
        }

        return new(gameVersion, loader, kind);
    }

    public static ResourceKind? ParseKind(string? kind)
    {
        if (string.IsNullOrWhiteSpace(kind))
        {
            return null;
        }

        return Enum.TryParse<ResourceKind>(kind, true, out var parsed)
            ? parsed
            : throw new CliException($"Package kind '{kind}' is not supported.", ExitCodes.Usage);
    }

    public static Profile.Rice.Entry FindEntry(Profile profile, string purl)
    {
        var parsed = ParsePurl(purl);
        return profile.Setup.Packages.FirstOrDefault(x =>
                PackageHelper.IsMatched(x.Purl, parsed.Label, parsed.Namespace, parsed.Pid)
            ) ?? throw new CliException($"Package '{purl}' is not installed.", ExitCodes.NotFound);
    }

    public static bool ContainsProject(Profile profile, string purl)
    {
        var parsed = ParsePurl(purl);
        return profile.Setup.Packages.Any(x =>
            PackageHelper.IsMatched(x.Purl, parsed.Label, parsed.Namespace, parsed.Pid)
        );
    }

    public static string ToPurl(Dependency dependency) =>
        PackageHelper.ToPurl(
            dependency.Label,
            dependency.Namespace,
            dependency.ProjectId,
            dependency.VersionId
        );
}
