using TridentCore.Pref.Building;

namespace TridentCore.Pref;

public readonly record struct PackageIdentifier(string Repository, string? Namespace, string Identity, string? Version)
{
    public override string ToString() => Builder.Build(Repository, Namespace, Identity, Version);
}
