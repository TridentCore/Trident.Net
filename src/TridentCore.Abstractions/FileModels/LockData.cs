using System.Text.Json.Serialization;
using TridentCore.Abstractions.Repositories.Resources;
using TridentCore.Abstractions.Utilities;

namespace TridentCore.Abstractions.FileModels;

// Platform records package compatibility; artifact regions cache their own inputs independently.
public record LockData
{
    public const int FORMAT = 9;

    public required PlatformData Platform { get; init; }
    public ArtifactData? Artifact { get; init; }
    public ArtifactRegion? Vanilla { get; init; }
    public ArtifactRegion? Loader { get; init; }
    public ArtifactRegion? Launch { get; init; }
    public IReadOnlyList<LockedPackage> Packages { get; init; } = [];
    public string? PackagesInput { get; init; }
    public string? PackageSource { get; init; }
    public IReadOnlyList<string> PackageSourceOrders { get; init; } = [];

    public record ArtifactRegion(string Input, ArtifactData Output);

    public uint? RuntimeMajor { get; init; }
    public RuntimeIndexReference? RuntimeIndex { get; init; }

    public record RuntimeIndexReference(Uri Url, FileHash? Hash);

    #region Nested type: PlatformData

    // NOTE: 内联值比较 record；LoadLock 恒提供它，阶段间用 == 比较。
    public record PlatformData(string Minecraft, string? Loader);

    #endregion

    #region Nested type: ArtifactData

    // The effective launch data and each preceding region use the same portable representation.
    public record ArtifactData(
        string MainClass,
        IReadOnlyList<uint> CompatibleJavaMajors,
        IReadOnlyList<string[]> GameArguments,
        IReadOnlyList<string[]> JavaArguments,
        IReadOnlyList<Library> Libraries,
        AssetData AssetIndex)
    {
        public Library? MainJar { get; init; }
        public IReadOnlyList<Agent> Agents { get; init; } = [];

        // NOTE: 兼容旧的单个 major 写法。读取时折叠成单元素集合；写出恒为 null，所以不再落盘。
        //  移除见 POLY-167。
        [Obsolete("compat: legacy scalar javaMajorVersion, remove once on-disk lock files have migrated (POLY-167)")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public uint? JavaMajorVersion
        {
            get => null;
            init => CompatibleJavaMajors = value is { } major ? [major] : [];
        }
    }

    public record Agent(Library Library, string? Arguments = null);

    #endregion

    #region Nested type: LockedPackage

    // NOTE: 保留所有来源的解析结果；离线仲裁不得重新解析 floating pref。
    public record LockedPackage(
        string Pref,
        string? Source,
        Package Resolved,
        PackageRule Rule)
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

    // WARNING: 锁定时刻冻结的规则评估结果。按包存储，规则微调只重算受影响包、绝不重解析（
    //  重解析会漂移 floating pref）。
    public record PackageRule(bool Skipping, string? Destination, bool Normalizing);

    #endregion

    #region Nested type: AssetData

    public record AssetData(string Id, Uri Url, FileHash? Hash);

    #endregion

    #region Nested type: Library

    // NOTE: IsNative 决定是否解压到 Natives 目录，IsPresent 决定是否加入 ClassPath，两者互不干扰。
    public record Library(Library.Identity Id, Uri? Url, FileHash? Hash, bool IsNative = false, bool IsPresent = true)
    {
        public string? LocalPath { get; init; }
        public string? CacheKey { get; init; }
        public IReadOnlyList<string> Exclude { get; init; } = [];
        #region Nested type: Identity

        public record Identity(string Namespace, string Name, string Version, string? Platform, string Extension);

        #endregion
    }

    #endregion

}
