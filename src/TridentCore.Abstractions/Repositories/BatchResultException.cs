namespace TridentCore.Abstractions.Repositories;

public class BatchResultException<TIdentifier>(IReadOnlyDictionary<TIdentifier, Exception> failures)
    : Exception($"Batch failed for {failures.Count} item(s): {string.Join(", ", failures.Keys)}")
{
    public IReadOnlyDictionary<TIdentifier, Exception> Failures { get; } = failures;
}
