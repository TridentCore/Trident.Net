namespace TridentCore.Abstractions.Adapters;

public record MigrateResult(IReadOnlyList<MigrateResult.Entry> Entries)
{
    public record Entry(string Name, bool Succeeded, string? Failure = null);
}
