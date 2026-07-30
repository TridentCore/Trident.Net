namespace TridentCore.Abstractions.Adapters;

public interface ILauncherAdapter
{
    // The launcher brands this adapter can parse. A single adapter may serve several brands that share
    // one instance format (e.g. the MultiMC family) — consumers dispatch by LauncherKind and never need
    // to know which adapter handles it.
    IReadOnlyList<LauncherKind> SupportedKinds { get; }

    // The brand's conventional data directory on the current platform for the given kind, or null when
    // none is known / present. Consumers use it to prefill the directory picker.
    string? DefaultDataDirectory(LauncherKind kind);

    // Coarse scan: enumerate instances under rootDir and parse their metadata (name, version,
    // loader) plus file-layout pointers. Reads metadata files only — no file hashing, no network.
    // Corrupt instances are returned too, flagged via LauncherInstance.IsCorrupt, so the UI can
    // surface them with the offending directory name and reason instead of silently dropping them.
    Task<IReadOnlyList<LauncherInstance>> ScanAsync(string rootDir, CancellationToken cancellationToken = default);
}
