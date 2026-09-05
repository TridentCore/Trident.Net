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
