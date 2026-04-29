using Trident.Abstractions.FileModels;
using Trident.Abstractions.Repositories.Resources;
using Trident.Abstractions.Utilities;

namespace Trident.Cli.Commands.Package;

internal static class PackageDtos
{
    public static LocalPackageDto FromEntry(Profile.Rice.Entry entry) =>
        new(entry.Purl, entry.Enabled, entry.Source, entry.Tags.ToArray());

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

    public static PackageDto FromPackage(Trident.Abstractions.Repositories.Resources.Package package) =>
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

    public static VersionDto FromVersion(Trident.Abstractions.Repositories.Resources.Version version) =>
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

    public static DependencyDto FromDependency(Trident.Abstractions.Repositories.Resources.Dependency dependency) =>
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
