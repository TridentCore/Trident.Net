using System.Text.Json.Serialization;
using TridentCore.Abstractions.Repositories.Resources;
using TridentCore.Abstractions.Utilities;

namespace TridentCore.Abstractions.FileModels;

// NOTE: Version-locking source of truth——Platform 是声明意图（来自 Profile），Artifact 是平台计算的
//  构建缓存（vanilla + loader），Packages 是解析并锁定的依赖。可跨机器迁移（无本地标识）。
public record LockData
{
    public const int FORMAT = 2;

    public required PlatformData Platform { get; init; }
    public required ViabilityData Viability { get; init; }
    public ArtifactData? Artifact { get; init; }
    public IReadOnlyList<LockedPackage> Packages { get; init; } = [];

    public RuntimeData? Runtime { get; init; }

    #region Nested type: PlatformData

    // NOTE: 内联值比较 record；LoadLock 恒提供它，阶段间用 == 比较。
    public record PlatformData(string Minecraft, string? Loader);

    #endregion

    #region Nested type: ViabilityData

    // NOTE: 控制缓存有效性的 hash 指纹。新增 xxxHash 字段放这里，不要放顶层。
    public record ViabilityData(string OptionsHash, string? PriorityHash = null);

    #endregion

    #region Nested type: ArtifactData

    // NOTE: 平台计算出的构建缓存（vanilla + loader 参数/库/assets）。随平台整体生灭：
    //  平台匹配时原子迁移，不匹配时按步骤（先 vanilla 后 loader）重建。
    public record ArtifactData(
        string MainClass,
        uint JavaMajorVersion,
        IReadOnlyList<string> GameArguments,
        IReadOnlyList<string> JavaArguments,
        IReadOnlyList<Library> Libraries,
        AssetData AssetIndex);

    #endregion

    #region Nested type: LockedPackage

    // NOTE: 声明的 pref 与其解析锁定的 Package、锁定时的规则结果。pref 是 diff 键（声明意图，可能
    //  floating）；Resolved 原样保存完整解析结果，规则重算、manifest 生成与宿主 UI 都不再命中仓库。
    //
    //  SuppressedBy 指认 FlattenPackages 中赢得目标路径仲裁的 pref；被抑制的包保持锁定，
    //  优先级重排后其版本仍在而不必重解析（null = 生效，将物化进 build）。
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

    // NOTE: 锁定时刻冻结的规则评估结果。按包存储，规则微调只重算受影响包、绝不重解析（
    //  重解析会漂移 floating pref）。
    public record PackageRule(bool Skipping, string? Destination, bool Normalizing);

    #endregion

    #region Nested type: AssetData

    public record AssetData(string Id, Uri Url, FileHash? Hash);

    #endregion

    #region Nested type: Library

    // NOTE: IsNative 决定是否解压到 Natives 目录，IsPresent 决定是否加入 ClassPath，两者互不干扰。
    public record Library(Library.Identity Id, Uri Url, FileHash? Hash, bool IsNative = false, bool IsPresent = true)
    {
        #region Nested type: Identity

        public record Identity(string Namespace, string Name, string Version, string? Platform, string Extension);

        #endregion
    }

    #endregion

    #region Nested type: RuntimeData

    // NOTE: 缓存在 runtimes/{major}.json 的运行时 manifest 指纹。EnsureRuntimeStage 凭 sha1 匹配
    //  离线复用缓存而非每次部署都拉 Mojang 运行时索引。随 artifact 迁移：平台（Java 大版本）不变则原子迁移，变则重建。
    public record RuntimeData(uint Major, string Sha1);

    #endregion
}
