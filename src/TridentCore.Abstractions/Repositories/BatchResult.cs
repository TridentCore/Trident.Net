namespace TridentCore.Abstractions.Repositories;

public sealed record BatchResult<TIdentifier, TItem>(
    IReadOnlyDictionary<TIdentifier, TItem> Successful,
    IReadOnlyDictionary<TIdentifier, Exception> Failed) where TIdentifier : notnull
{
    public bool HasFailures => Failed.Count > 0;

    public void ThrowIfFailures()
    {
        if (HasFailures)
        {
            throw new BatchResultException<TIdentifier>(Failed);
        }
    }

    public BatchResult<TMappedIdentifier, TItem> MapKeys<TMappedIdentifier>(
        Func<TIdentifier, TMappedIdentifier> map) where TMappedIdentifier : notnull =>
        new(Successful.ToDictionary(x => map(x.Key), x => x.Value), Failed.ToDictionary(x => map(x.Key), x => x.Value));

    public static BatchResult<TIdentifier, TItem> FromFailures(
        IEnumerable<TIdentifier> identifiers,
        Exception error) =>
        new(new Dictionary<TIdentifier, TItem>(), identifiers.Distinct().ToDictionary(x => x, _ => error));
}
