namespace TridentCore.Abstractions.FileModels;

public record PatchIndex
{
    public int Format { get; init; } = 1;
    public IReadOnlyList<Entry> Import { get; init; } = [];
    public IReadOnlyList<Entry> Users { get; init; } = [];

    public record Entry(string Path, bool Enabled = true);
}
