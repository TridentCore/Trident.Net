using TridentCore.Pref.Building;

namespace TridentCore.Pref;

public readonly record struct ProjectIdentifier(
    string Repository,
    string? Namespace,
    string Identity
)
{
    public override string ToString() => Builder.Build(Repository, Namespace, Identity, null);
}
