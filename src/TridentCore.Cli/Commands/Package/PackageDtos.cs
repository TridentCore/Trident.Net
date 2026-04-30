using TridentCore.Abstractions.FileModels;
using TridentCore.Abstractions.Repositories.Resources;
using TridentCore.Abstractions.Utilities;
using TridentCore.Cli.Services;
using TridentCore.Core.Services;

namespace TridentCore.Cli.Commands.Package;

internal static class PackageDtos
{
    public static LocalPackageDto FromEntry(Profile.Rice.Entry entry) =>
        new(entry.Purl, entry.Enabled, entry.Source, entry.Tags.ToArray());

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
            .Select(e => PackageHelper.TryParse(e.Purl, out var p) ? p : default)
            .Where(p => p.Label is not null)
            .Select(p => (p.Label, p.Namespace, p.Pid))
            .ToList();

        var filter = PackageCliHelper.BuildFilter(null, null, null, instance);
        var projects = await repositories.QueryBatchAsync(batch).ConfigureAwait(false);

        var projectLookup = projects.ToDictionary(
            p => PackageHelper.ToPurl(p.Label, p.Namespace, p.ProjectId, null),
            StringComparer.OrdinalIgnoreCase
        );

        return entryList
            .Select(e =>
            {
                var key = PackageHelper.ExtractProjectIdentityIfValid(e.Purl);
                var project = projectLookup.GetValueOrDefault(key);
                return new ResolvedLocalPackageDto(
                    e.Purl,
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
            PackageHelper.ToPurl(exhibit.Label, exhibit.Namespace, exhibit.Pid, null),
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

    public static PackageDto FromPackage(TridentCore.Abstractions.Repositories.Resources.Package package) =>
        new(
            PackageHelper.ToPurl(
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
            package.Sha1,
            package.Dependencies.Select(FromDependency).ToArray()
        );

    public static VersionDto FromVersion(TridentCore.Abstractions.Repositories.Resources.Version version) =>
        new(
            PackageHelper.ToPurl(
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

    public static DependencyDto FromDependency(TridentCore.Abstractions.Repositories.Resources.Dependency dependency) =>
        new(
            PackageHelper.ToPurl(
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
    string Purl,
    bool Enabled,
    string? Source,
    IReadOnlyList<string> Tags
);

internal sealed record ResolvedLocalPackageDto(
    string Purl,
    bool Enabled,
    string? Source,
    IReadOnlyList<string> Tags,
    string? ProjectName,
    string? Author,
    string? Summary,
    ResourceKind? Kind
);

internal sealed record ExhibitDto(
    string Purl,
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
    string Purl,
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
    string Purl,
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
    string Purl,
    string Label,
    string? Namespace,
    string ProjectId,
    string? VersionId,
    bool IsRequired
);
