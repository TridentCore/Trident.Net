using System.Text.Json.Serialization;

namespace TridentCore.Abstractions.Launching;

public sealed record LaunchSelection
{
    public Binding From { get; init; } = Binding.Component;
    public string? Id { get; init; }
    public string? Version { get; init; }
    public bool Enabled { get; init; } = true;

    [JsonConverter(typeof(JsonStringEnumConverter<Binding>))]
    public enum Binding { Component, Minecraft, Loader }
}
