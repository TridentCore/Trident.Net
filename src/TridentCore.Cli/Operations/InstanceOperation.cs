using TridentCore.Abstractions;
using TridentCore.Cli.Commands.Package;
using TridentCore.Cli.Services;
using TridentCore.Cli.Utilities;
using TridentCore.Core.Services;

namespace TridentCore.Cli.Operations;

internal static class InstanceOperation
{
    public static IReadOnlyList<InstanceSummary> List(ProfileManager profileManager)
    {
        return profileManager
            .Profiles.Select(x => new InstanceSummary(
                x.Item1,
                x.Item2.Name,
                x.Item2.Setup.Version,
                x.Item2.Setup.Loader,
                x.Item2.Setup.Source,
                x.Item2.Setup.Packages.Count,
                PathDef.Default.DirectoryOfHome(x.Item1)
            ))
            .OrderBy(x => x.Key, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    public static async Task<InstanceDetail> Inspect(
        InstanceContextResolver resolver,
        RepositoryAgent repositories,
        string instance,
        string? profile)
    {
        const int previewLimit = 5;
        var ctx = resolver.Resolve(instance, profile);
        var entries = ctx.Profile.Setup.Packages;
        var preview = entries.Take(previewLimit).ToList();

        var resolved = preview.Count > 0
            ? await PackageDtos.ResolveEntriesAsync(preview, repositories, ctx).ConfigureAwait(false)
            : [];

        return new(
            ctx.Key,
            ctx.Profile.Name,
            ctx.Profile.Setup.Version,
            ctx.Profile.Setup.Loader,
            ctx.Profile.Setup.Source,
            ctx.InstancePath,
            ctx.ProfilePath,
            entries.Count,
            [.. resolved.Select(p => new PackagePreview(p.Purl, p.Enabled, p.Source, p.Tags, p.ProjectName, p.Author, p.Kind))],
            Math.Max(0, entries.Count - previewLimit)
        );
    }
}

public sealed record InstanceSummary(
    string Key,
    string Name,
    string Version,
    string? Loader,
    string? Source,
    int PackageCount,
    string Path
);

public sealed record InstanceDetail(
    string Key,
    string Name,
    string Version,
    string? Loader,
    string? Source,
    string InstancePath,
    string ProfilePath,
    int PackageCount,
    IReadOnlyList<PackagePreview> Packages,
    int HiddenPackageCount
);

public sealed record PackagePreview(
    string Purl,
    bool Enabled,
    string? Source,
    IReadOnlyList<string> Tags,
    string? ProjectName,
    string? Author,
    TridentCore.Abstractions.Repositories.Resources.ResourceKind? Kind
) : IPackageTableRow;
