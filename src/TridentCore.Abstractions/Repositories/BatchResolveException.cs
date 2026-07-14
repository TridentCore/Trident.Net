namespace TridentCore.Abstractions.Repositories;

public class BatchResolveException<TIdentifier>(
    IReadOnlyDictionary<TIdentifier, Exception> failures
) : Exception(
    $"Batch resolve failed for {failures.Count} item(s): {string.Join(", ", failures.Keys)}"
)
{
    public IReadOnlyDictionary<TIdentifier, Exception> Failures { get; } = failures;
}
