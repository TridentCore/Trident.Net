using System.Collections.Immutable;

namespace TridentCore.Purl;

public readonly record struct PackageDescriptor(
    string Repository,
    string? Namespace,
    string Identity,
    string? Version,
    ImmutableArray<(string, string?)> Filters
);
