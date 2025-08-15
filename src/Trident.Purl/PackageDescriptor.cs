using System.Collections.Immutable;

namespace Trident.Purl
{
    public readonly record struct PackageDescriptor(
        string Repository,
        string? Namespace,
        string ProjectId,
        string? VersionId,
        ImmutableArray<(string, string)> Filters);
}