using System.Collections.Immutable;

namespace Trident.Purl;

public record PackageDescriptor(
    string Repository,
    string? Namespace,
    string Identity,
    string? Version,
    ImmutableArray<(string, string?)> Filters);
