using System.Collections.Immutable;
using TridentCore.Pref.Building;

namespace TridentCore.Pref;

public readonly record struct PackageDescriptor(
    string Repository,
    string? Namespace,
    string Identity,
    string? Version,
    ImmutableArray<(string, string?)> Filters
)
{
    public override string ToString() => Builder.Build(Repository, Namespace, Identity, Version, Filters.AsSpan());
}
