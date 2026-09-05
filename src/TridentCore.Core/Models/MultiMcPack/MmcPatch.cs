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

        // WARNING: MMC 的 "url" 是 maven 仓库根，不是文件地址——消费时必须接上坐标路径。
        //  完整地址只出现在下面的 MMC-absoluteUrl 私有扩展键里。
        [JsonPropertyName("url")]
        public Uri? RepositoryUrl { get; init; }

        [JsonPropertyName("MMC-absoluteUrl")]
        public Uri? AbsoluteUrl { get; init; }

        // MultiMC 早期版本的拼写错误，Prism 至今仍兼容读取。
        [JsonPropertyName("MMC-absulute_url")]
        public Uri? LegacyAbsoluteUrl { get; init; }

        // 上游只识别 "local"（文件已在实例 libraries/ 内，不下载）与 "always-stale"（忽略缓存重下）。
        [JsonPropertyName("MMC-hint")]
        public string? Hint { get; init; }

        // 显式覆写由坐标推导出的文件名。
        [JsonPropertyName("MMC-filename")]
        public string? FileName { get; init; }

        public MmcDownloads? Downloads { get; init; }
        public MmcNatives? Natives { get; init; }

        public sealed record MmcDownloads(MmcArtifact? Artifact);

        public sealed record MmcArtifact(string Sha1, ulong Size, Uri Url);

        public sealed record MmcNatives(string? Windows, string? Linux, string? Osx);
    }
}
