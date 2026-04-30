namespace TridentCore.Purl;

public readonly record struct ScopedPackageIdentifier(
    string? Namespace,
    string Identity,
    string? Version
);
