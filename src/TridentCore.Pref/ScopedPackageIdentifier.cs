namespace TridentCore.Pref;

public readonly record struct ScopedPackageIdentifier(
    string? Namespace,
    string Identity,
    string? Version
);
