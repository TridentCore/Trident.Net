using System.Text.Json.Serialization;

namespace TridentCore.Abstractions.FileModels;

public record PatchArtifact
{
    public required string MainClass { get; init; }
    public IReadOnlyList<uint> CompatibleJavaMajors { get; init; } = [];
    public IReadOnlyList<string[]> GameArguments { get; init; } = [];
    public IReadOnlyList<string[]> JvmArguments { get; init; } = [];
    public bool DefaultJvmArguments { get; init; } = true;
    public IReadOnlyList<PatchLibrary> Libraries { get; init; } = [];
    public IReadOnlyList<PatchAgent> Agents { get; init; } = [];
    public PatchLibrary? MainJar { get; init; }
    public required LockData.AssetData AssetIndex { get; init; }

    // NOTE: 兼容旧的单个 major 写法。读取时折叠成单元素集合；写出恒为 null，所以不再落盘。
    //  移除见 POLY-167。
    [Obsolete("compat: legacy scalar javaMajor, remove once on-disk patch files have migrated (POLY-167)")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public uint? JavaMajor
    {
        get => null;
        init => CompatibleJavaMajors = value is { } major ? [major] : [];
    }
}
