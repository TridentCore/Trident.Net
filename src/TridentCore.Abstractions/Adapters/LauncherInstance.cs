namespace TridentCore.Abstractions.Adapters;

public record LauncherInstance
{
    public required LauncherKind Kind { get; init; }

    // 实例在启动器根下的目录名；Trident 实例 key 的候选。
    public required string Key { get; init; }

    // 实例主目录的绝对路径（启动器元数据文件所在）。
    public required string HomeDirectory { get; init; }

    public string? Name { get; init; }
    public string? MinecraftVersion { get; init; }

    // 以 Trident lurl 表达的 loader；vanilla 为 null。
    public string? Loader { get; init; }

    // 元数据未能完整解析时非 null，值为失败原因。
    public CorruptReason? CorruptReason { get; init; }

    // 运行目录绝对路径（.minecraft 等价物）——build/ 的拷贝源。
    public required string RuntimeDirectory { get; init; }

    // 参与批量识别的运行目录子目录名（mods、resourcepacks、shaderpacks 等）；
    // 命中的文件成为包引用，其余拷贝。
    public required IReadOnlyList<string> IdentifiableSubdirs { get; init; }
}
