using System.Text.Json;
using System.Text.Json.Serialization;

namespace TridentCore.Core.Models.PrismLauncherApi;

public record Component(
    [property: JsonPropertyName("+tweakers")]
    IReadOnlyList<string>? Tweakers,
    [property: JsonPropertyName("+traits")]
    IReadOnlyList<string>? Traits,
    Component.AssetIndexEntry? AssetIndex,
    IReadOnlyList<uint>? CompatibleJavaMajors,
    int FormatVersion,
    IReadOnlyList<Component.Library>? Libraries,
    IReadOnlyList<Component.Library>? MavenFiles,
    string? MainClass,
    Component.Library? MainJar,
    string? MinecraftArguments,
    string Name,
    int Order,
    DateTimeOffset ReleaseTime,
    IReadOnlyList<Component.Requirement> Requires,
    string Type,
    string Uid,
    string Version)
{
    /// <summary>
    ///     本地组件定义（`patches/{uid}.json`）可能携带远端 meta 之外的字段；只做「存在即拒绝」判断的
    ///     字段留在这里，不逐个建模。
    /// </summary>
    [JsonExtensionData]
    public IDictionary<string, JsonElement>? ExtraMembers { get; init; }

    [JsonPropertyName("+jvmArgs")]
    public IReadOnlyList<string>? JvmArguments { get; init; }

    [JsonPropertyName("+libraries")]
    public IReadOnlyList<Component.Library>? ExtraLibraries { get; init; }

    [JsonPropertyName("+agents")]
    public IReadOnlyList<Component.Library>? Agents { get; init; }

    /// <summary>Mojang 风格的 `downloads` 表（`client` 等条目），仅本地 net.minecraft 定义使用。</summary>
    public IDictionary<string, Component.Library.DownloadsEntry.ArtifactEntry>? Downloads { get; init; }

    #region Nested type: AssetIndexEntry

    public record AssetIndexEntry(string Id, string Sha1, ulong Size, ulong TotalSize, Uri Url);

    #endregion

    #region Nested type: Library

    public record Library(
        Library.DownloadsEntry? Downloads,
        Library.ExtractExtry? Extract,
        string Name,
        Uri? Url,
        Library.NativesEntry? Natives,
        IReadOnlyList<Library.Rule>? Rules)
    {
        [JsonPropertyName("MMC-hint")]
        public string? Hint { get; init; }

        [JsonPropertyName("MMC-filename")]
        public string? FileName { get; init; }

        [JsonPropertyName("MMC-absoluteUrl")]
        public Uri? AbsoluteUrl { get; init; }

        // NOTE: Prism 同时读取这个拼写错误的历史字段，导出只写正确拼写。
        [JsonPropertyName("MMC-absulute_url")]
        public Uri? MisspelledAbsoluteUrl { get; init; }

        /// <summary>`+agents` 条目与 library 同形，额外带这个可选参数。</summary>
        public string? Argument { get; init; }

        #region Nested type: DownloadsEntry

        public record DownloadsEntry(
            DownloadsEntry.ArtifactEntry? Artifact,
            IReadOnlyDictionary<string, DownloadsEntry.ArtifactEntry>? Classifiers)
        {
            #region Nested type: ArtifactEntry

            public record ArtifactEntry(string Sha1, ulong Size, Uri Url);

            #endregion
        }

        #endregion

        #region Nested type: ExtractExtry

        public record ExtractExtry(IReadOnlyList<string> Exclude);

        #endregion

        #region Nested type: NativesEntry

        public record NativesEntry(string? Windows, string? Linux, string? Osx);

        #endregion

        #region Nested type: Rule

        public record Rule(string Action, IReadOnlyDictionary<string, string>? Os)
        {
            [JsonExtensionData]
            public IDictionary<string, JsonElement>? ExtraMembers { get; init; }
        }

        #endregion
    }

    #endregion

    #region Nested type: Requirement

    public record Requirement(
        [property: JsonPropertyName("suggests")]
        string? Suggest,
        [property: JsonPropertyName("equals")] string? Equal,
        string Uid);

    #endregion
}
