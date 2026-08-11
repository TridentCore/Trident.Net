namespace TridentCore.Abstractions.Adapters;

public interface ILauncherAdapter
{
    // NOTE: 该适配器可解析的启动器品牌。单个适配器可服务共享同一实例格式的多个品牌
    //  （如 MultiMC 家族）——消费方按 LauncherKind 分发，无需知道谁在处理。
    IReadOnlyList<LauncherKind> SupportedKinds { get; }

    // NOTE: 该品牌在当前平台上的常规数据目录，未知/不存在时为 null；消费方用于预填目录选择器。
    string? DefaultDataDirectory(LauncherKind kind);

    // NOTE: 粗扫——枚举 rootDir 下实例并解析元数据（名称、版本、loader）与文件布局指针。
    //  只读元数据文件——无哈希、无网络。损坏实例也返回（CorruptReason 标记），
    //  UI 可带着目录名与原因呈现，而非静默丢弃。
    Task<IReadOnlyList<LauncherInstance>> ScanAsync(string rootDir, CancellationToken cancellationToken = default);
}
