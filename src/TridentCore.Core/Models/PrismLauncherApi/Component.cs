using System.Text.Json;
using System.Text.Json.Serialization;

namespace TridentCore.Core.Models.PrismLauncherApi;

public sealed record Component
{
    public int FormatVersion { get; init; } = 1;
    public string Uid { get; init; } = "";
    public string? Version { get; init; }
    public string? Name { get; init; }
    public string? MainClass { get; init; }
    public string? MinecraftArguments { get; init; }
    public Dictionary<string, JsonElement[]>? Arguments { get; init; }
    public AssetIndexEntry? AssetIndex { get; init; }
    public IReadOnlyList<uint>? CompatibleJavaMajors { get; init; }
    public IReadOnlyList<Library>? Libraries { get; init; }
    public IReadOnlyList<Library>? MavenFiles { get; init; }
    public Library? MainJar { get; init; }
    public IReadOnlyList<Requirement> Requires { get; init; } = [];
    public IReadOnlyList<Requirement> Conflicts { get; init; } = [];
    public IReadOnlyList<Library>? JarMods { get; init; }

    [JsonPropertyName("+libraries")]
    public IReadOnlyList<Library>? AdditionalLibraries { get; init; }
    [JsonPropertyName("+jvmArgs")]
    public IReadOnlyList<string>? JvmArguments { get; init; }
    [JsonPropertyName("+tweakers")]
    public IReadOnlyList<string>? Tweakers { get; init; }
    [JsonPropertyName("+traits")]
    public IReadOnlyList<string>? Traits { get; init; }
    [JsonPropertyName("+agents")]
    public IReadOnlyList<Library>? Agents { get; init; }
    [JsonExtensionData]
    public Dictionary<string, JsonElement>? Extra { get; init; }

    public sealed record AssetIndexEntry(string Id, string? Sha1, ulong Size, ulong TotalSize, Uri Url);

    public sealed record Library
    {
        public string Name { get; init; } = "";
        public Uri? Url { get; init; }
        public DownloadsEntry? Downloads { get; init; }
        public IReadOnlyDictionary<string, string>? Natives { get; init; }
        public IReadOnlyList<Rule>? Rules { get; init; }
        public ExtractEntry? Extract { get; init; }
        [JsonPropertyName("MMC-hint")]
        public string? Hint { get; init; }
        [JsonPropertyName("MMC-filename")]
        public string? FileName { get; init; }
        [JsonPropertyName("MMC-absoluteUrl")]
        public Uri? AbsoluteUrl { get; init; }
        [JsonPropertyName("MMC-absulute_url")]
        public Uri? LegacyAbsoluteUrl { get; init; }

        public sealed record DownloadsEntry
        {
            public ArtifactEntry? Artifact { get; init; }
            public IReadOnlyDictionary<string, ArtifactEntry> Classifiers { get; init; } = new Dictionary<string, ArtifactEntry>();
            public sealed record ArtifactEntry(string? Sha1, ulong Size, Uri Url);
        }
        public sealed record ExtractEntry(IReadOnlyList<string> Exclude);
        public sealed record Rule(string Action, IReadOnlyDictionary<string, string>? Os,
                                  IReadOnlyDictionary<string, bool>? Features = null);
    }

    public sealed record Requirement(
        string Uid,
        [property: JsonPropertyName("equals")] string? Equal = null,
        [property: JsonPropertyName("suggests")] string? Suggest = null);
}
