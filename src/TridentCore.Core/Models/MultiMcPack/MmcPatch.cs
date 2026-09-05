using System.Text.Json.Serialization;

namespace TridentCore.Core.Models.MultiMcPack;

public sealed record MmcPatch
{
    public string? Uid { get; init; }
    public string? MainClass { get; init; }
    public string? MinecraftArguments { get; init; }

    [JsonPropertyName("+jvmArgs")]
    public IReadOnlyList<string>? JvmArguments { get; init; }

    [JsonPropertyName("+tweakers")]
    public IReadOnlyList<string>? Tweakers { get; init; }

    public IReadOnlyList<uint>? CompatibleJavaMajors { get; init; }

    [JsonPropertyName("+traits")]
    public IReadOnlyList<string>? Traits { get; init; }

    public IReadOnlyList<MmcLibrary>? Libraries { get; init; }

    [JsonPropertyName("+libraries")]
    public IReadOnlyList<MmcLibrary>? AdditionalLibraries { get; init; }

    // Prism 将 mavenFiles 下载到库目录但不置入 classpath（Forge 安装器等）。
    public IReadOnlyList<MmcLibrary>? MavenFiles { get; init; }

    // 1.7.10 时代的 mod 注入方式：把条目重打包进客户端 jar。原生计划只表达启动行为，
    // 不做 jar 重打包；读入仅为了能在转换时如实告知用户这部分无法携带。
    public IReadOnlyList<MmcLibrary>? JarMods { get; init; }

    [JsonPropertyName("+agents")]
    public IReadOnlyList<MmcLibrary>? Agents { get; init; }

    public MmcAssetIndex? AssetIndex { get; init; }

    public sealed record MmcAssetIndex(string Id, string Sha1, ulong Size, ulong TotalSize, Uri Url);

    public sealed record MmcLibrary
    {
        public string Name { get; init; } = "";
        public Uri? Url { get; init; }
        public MmcDownloads? Downloads { get; init; }
        public MmcNatives? Natives { get; init; }

        public sealed record MmcDownloads(MmcArtifact? Artifact);

        public sealed record MmcArtifact(string Sha1, ulong Size, Uri Url);

        public sealed record MmcNatives(string? Windows, string? Linux, string? Osx);
    }
}
