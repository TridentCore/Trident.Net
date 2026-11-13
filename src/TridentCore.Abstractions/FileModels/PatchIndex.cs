namespace TridentCore.Abstractions.FileModels;

public record PatchIndex
{
    public const int FORMAT = 1;

    public int Format { get; init; } = FORMAT;
    public IReadOnlyList<Entry> Import { get; init; } = [];
    public IReadOnlyList<Entry> Users { get; init; } = [];

    public record Entry(string Path, bool Enabled = true);
}
