using System.Text.Json.Serialization;
using TridentCore.Abstractions.Repositories.Resources;
using TridentCore.Abstractions.Utilities;

namespace TridentCore.Abstractions.FileModels;

// Version-locking source of truth: Platform is the declared intent (from Profile),
// Artifact is the platform-computed build cache (vanilla + loader), Packages are the
// resolved-and-locked dependencies. Migratable across machines (no local identifiers).
public record LockData
{
    public const int FORMAT = 2;

    public required PlatformData Platform { get; init; }
    public required ViabilityData Viability { get; init; }
    public ArtifactData? Artifact { get; init; }
    public IReadOnlyList<LockedPackage> Packages { get; init; } = [];

    public RuntimeData? Runtime { get; init; }

    #region Nested type: PlatformData

    // Inline value-compared record; LoadLock always supplies it, so stages compare with ==.
    public record PlatformData(string Minecraft, string? Loader);

    #endregion

    #region Nested type: ViabilityData

    // Hash fingerprints governing cache validity. New xxxHash fields go here, not at top level.
    public record ViabilityData(string OptionsHash, string? PriorityHash = null);

    #endregion

    #region Nested type: ArtifactData

    // Platform-computed build cache (vanilla + loader args/libs/assets). Lives and dies with
    // the platform as a whole: migrated atomically when the platform matches, rebuilt in steps
    // (vanilla then loader) when it does not.
    public record ArtifactData(
        string MainClass,
        uint JavaMajorVersion,
        IReadOnlyList<string> GameArguments,
        IReadOnlyList<string> JavaArguments,
        IReadOnlyList<Library> Libraries,
        AssetData AssetIndex);

    #endregion

    #region Nested type: LockedPackage

    // A declared pref paired with its resolved-and-locked Package and the rule outcome at lock
    // time. The pref is the diff key (declared intent, possibly floating); Resolved is the full
    // resolved Package stored verbatim so rule recompute, manifest generation, and the host UI
    // all read real data without re-hitting repositories.
    //
    // SuppressedBy names the pref that won the target-path arbitration in FlattenPackages; a
    // suppressed package stays locked so its version survives a later priority reshuffle
    // without re-resolving (null = effective, will be materialized into the build).
    public record LockedPackage(
        string Pref,
        string? Source,
        Package Resolved,
        PackageRule Rule,
        string? SuppressedBy = null)
    {
        [Obsolete("compat: legacy purl key, remove once on-disk lock files have migrated")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public string? Purl
        {
            get => null;
            init => Pref = PackageHelper.SafeMigrate(value);
        }
    }

    #endregion

    #region Nested type: PackageRule

    // The rule evaluation outcome frozen into the lock. Per-package so a rule tweak only
    // recomputes the affected packages and never re-resolves (which would drift floating prefs).
    public record PackageRule(bool Skipping, string? Destination, bool Normalizing);

    #endregion

    #region Nested type: AssetData

    public record AssetData(string Id, Uri Url, FileHash? Hash);

    #endregion

    #region Nested type: Library

    // IsNative 决定是否解压到 Natives 目录，IsPresent 决定是否添加到 ClassPath，两者互不干扰
    public record Library(Library.Identity Id, Uri Url, FileHash? Hash, bool IsNative = false, bool IsPresent = true)
    {
        #region Nested type: Identity

        public record Identity(string Namespace, string Name, string Version, string? Platform, string Extension);

        #endregion
    }

    #endregion

    #region Nested type: RuntimeData

    // Fingerprint of the bundled runtime manifest cached at runtimes/{major}.json. Lets
    // EnsureRuntimeStage reuse the cached manifest offline (sha1 match) instead of re-fetching
    // Mojang's runtime index every deploy. Travels with the artifact: migrated atomically when
    // the platform is unchanged (same Java major), rebuilt when it changes.
    public record RuntimeData(uint Major, string Sha1);

    #endregion
}
