namespace TridentCore.Pref;

public readonly record struct ScopedProjectIdentifier(
    string? Namespace,
    string Identity
);
