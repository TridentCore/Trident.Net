using System.Text.Json.Serialization;

namespace TridentCore.Abstractions.Launching;

public sealed record LaunchLibrary(LaunchArtifact Artifact, LaunchLibrary.Usage Use = LaunchLibrary.Usage.Classpath)
{
    public IReadOnlyList<LaunchRule> Rules { get; init; } = [];
    public LaunchPlatform? Platform { get; init; }
    public IReadOnlyList<string> ExtractExcludes { get; init; } = [];
    public string? AgentArguments { get; init; }

    [JsonConverter(typeof(JsonStringEnumConverter<Usage>))]
    public enum Usage { Classpath, Client, Native, Required, Agent }
}
