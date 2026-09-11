namespace TridentCore.Abstractions.FileModels;

public record PatchAgent
{
    public required PatchLibrary Library { get; init; }
    public string? Arguments { get; init; }
}
