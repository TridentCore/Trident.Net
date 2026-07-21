using TridentCore.Pref;
using TridentCore.Abstractions.FileModels;
using TridentCore.Abstractions.Repositories.Resources;
using TridentCore.Abstractions.Utilities;
using TridentCore.Cli.Services;
using TridentCore.Cli.Utilities;
using TridentCore.Core.Services;

namespace TridentCore.Cli.Commands.Package;

internal static class PackageDtos
{
    public static LocalPackageDto FromEntry(Profile.Rice.Entry entry) =>
        new(entry.Pref, entry.Enabled, entry.Source, entry.Tags.ToArray());

    public static async Task<IReadOnlyList<ResolvedLocalPackageDto>> ResolveEntriesAsync(
        IEnumerable<Profile.Rice.Entry> entries,
        RepositoryAgent repositories,
        ResolvedInstanceContext instance
    )
    {
        var entryList = entries.ToList();
        if (entryList.Count == 0)
        {
            return [];
        }

        var batch = entryList
            .Select(e => PackageHelper.TryParse(e.Pref, out var p) ? p : default)
            .Where(p => p.Repository is not null)
            .Select(p => p.ToProjectIdentifier())
            .ToList();

        var filter = PackageCliHelper.BuildFilter(null, null, null, instance);
        var projects = await repositories.QueryBatchAsync(batch).ConfigureAwait(false);

        var projectLookup = projects.Successful.ToDictionary(
            p => PackageHelper.ToPref(p.Value.Label, p.Value.Namespace, p.Value.ProjectId, null),
            p => p.Value,
            StringComparer.OrdinalIgnoreCase
        );

        return entryList
            .Select(e =>
            {
                var key = PackageHelper.ExtractProjectIdentityIfValid(e.Pref);
                var project = projectLookup.GetValueOrDefault(key);
                return new ResolvedLocalPackageDto(
                    e.Pref,
                    e.Enabled,
                    e.Source,
                    e.Tags.ToArray(),
                    project?.ProjectName,
                    project?.Author,
                    project?.Summary,
                    project?.Kind
                );
            })
            .ToList();
    }

    public static ExhibitDto FromExhibit(Exhibit exhibit) =>
        new(
            PackageHelper.ToPref(exhibit.Label, exhibit.Namespace, exhibit.Pid, null),
            exhibit.Label,
            exhibit.Namespace,
            exhibit.Pid,
            exhibit.Name,
            exhibit.Author,
            exhibit.Summary,
            exhibit.Kind,
            exhibit.DownloadCount,
            exhibit.Tags,
            exhibit.Reference,
            exhibit.UpdatedAt
        );

    public static PackageDto FromPackage(
        TridentCore.Abstractions.Repositories.Resources.Package package
    ) =>
        new(
            PackageHelper.ToPref(
                package.Label,
                package.Namespace,
                package.ProjectId,
                package.VersionId
            ),
            package.Label,
            package.Namespace,
            package.ProjectId,
            package.VersionId,
            package.ProjectName,
            package.VersionName,
            package.Author,
            package.Summary,
            package.Kind,
            package.ReleaseType,
            package.PublishedAt,
            package.Download,
            package.Size,
            package.FileName,
            package.Hash?.Value,
            package.Dependencies.Select(FromDependency).ToArray()
        );

    public static VersionDto FromVersion(
        TridentCore.Abstractions.Repositories.Resources.Version version
    ) =>
        new(
            PackageHelper.ToPref(
                version.Label,
                version.Namespace,
                version.ProjectId,
                version.VersionId
            ),
            version.Label,
            version.Namespace,
            version.ProjectId,
            version.VersionId,
            version.VersionName,
            version.ReleaseType,
            version.PublishedAt,
            version.DownloadCount,
            version.Dependencies.Select(FromDependency).ToArray()
        );

    public static DependencyDto FromDependency(
        TridentCore.Abstractions.Repositories.Resources.Dependency dependency
    ) =>
        new(
            PackageHelper.ToPref(
                dependency.Label,
                dependency.Namespace,
                dependency.ProjectId,
                dependency.VersionId
            ),
            dependency.Label,
            dependency.Namespace,
            dependency.ProjectId,
            dependency.VersionId,
            dependency.IsRequired
        );
}

internal sealed record LocalPackageDto(
    string Pref,
    bool Enabled,
    string? Source,
    IReadOnlyList<string> Tags
);

internal sealed record ResolvedLocalPackageDto(
    string Pref,
    bool Enabled,
    string? Source,
    IReadOnlyList<string> Tags,
    string? ProjectName,
    string? Author,
    string? Summary,
    ResourceKind? Kind
) : IPackageTableRow;

internal sealed record ExhibitDto(
    string Pref,
    string Label,
    string? Namespace,
    string ProjectId,
    string Name,
    string Author,
    string Summary,
    ResourceKind Kind,
    ulong DownloadCount,
    IReadOnlyList<string> Tags,
    Uri Reference,
    DateTimeOffset UpdatedAt
);

internal sealed record PackageDto(
    string Pref,
    string Label,
    string? Namespace,
    string ProjectId,
    string VersionId,
    string ProjectName,
    string VersionName,
    string Author,
    string Summary,
    ResourceKind Kind,
    ReleaseType ReleaseType,
    DateTimeOffset PublishedAt,
    Uri Download,
    ulong Size,
    string FileName,
    string? Sha1,
    IReadOnlyList<DependencyDto> Dependencies
);

internal sealed record VersionDto(
    string Pref,
    string Label,
    string? Namespace,
    string ProjectId,
    string VersionId,
    string VersionName,
    ReleaseType ReleaseType,
    DateTimeOffset PublishedAt,
    ulong DownloadCount,
    IReadOnlyList<DependencyDto> Dependencies
);

internal sealed record DependencyDto(
    string Pref,
    string Label,
    string? Namespace,
    string ProjectId,
    string? VersionId,
    bool IsRequired
) : IDependencyTableRow;
