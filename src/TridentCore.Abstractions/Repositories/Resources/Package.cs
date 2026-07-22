using TridentCore.Abstractions.Utilities;
using FileHash = TridentCore.Abstractions.Utilities.FileHash;

namespace TridentCore.Abstractions.Repositories.Resources;

public record Package(
    string Label,
    string? Namespace,
    string ProjectId,
    string VersionId,
    string ProjectName,
    string VersionName,
    Uri? Thumbnail,
    string Author,
    string Summary,
    Uri Reference,
    ResourceKind Kind,
    ReleaseType ReleaseType,
    DateTimeOffset PublishedAt,
    Uri Download,
    ulong Size,
    string FileName,
    FileHash? Hash,
    Requirement Requirements,
    IReadOnlyList<Dependency> Dependencies)
{
    public override string ToString() => PackageHelper.ToPref(this);
}
