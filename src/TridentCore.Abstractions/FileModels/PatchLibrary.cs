using TridentCore.Abstractions.Utilities;

namespace TridentCore.Abstractions.FileModels;

public record PatchLibrary
{
    public required string Identity { get; init; }
    public Uri? Url { get; init; }
    public string? Local { get; init; }
    public FileHash? Hash { get; init; }
    public bool Native { get; init; }
    public bool Classpath { get; init; } = true;
    public IReadOnlyList<string> Exclude { get; init; } = [];
    public IReadOnlyList<PatchDocument.PlatformRule> Rules { get; init; } = [];
}
