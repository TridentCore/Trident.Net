namespace TridentCore.Abstractions.Repositories;

public sealed record BatchResolveResult<TIdentifier, TItem>(
    IReadOnlyDictionary<TIdentifier, TItem> Successful,
    IReadOnlyDictionary<TIdentifier, Exception> Failed) where TIdentifier : notnull
{
    public bool HasFailures => Failed.Count > 0;

    public void ThrowIfFailures()
    {
        if (HasFailures)
        {
            throw new BatchResolveException<TIdentifier>(Failed);
        }
    }

    public BatchResolveResult<TMappedIdentifier, TItem> MapKeys<TMappedIdentifier>(
        Func<TIdentifier, TMappedIdentifier> map) where TMappedIdentifier : notnull =>
        new(Successful.ToDictionary(x => map(x.Key), x => x.Value), Failed.ToDictionary(x => map(x.Key), x => x.Value));

    public static BatchResolveResult<TIdentifier, TItem> FromFailures(
        IEnumerable<TIdentifier> identifiers,
        Exception error) =>
        new(new Dictionary<TIdentifier, TItem>(), identifiers.Distinct().ToDictionary(x => x, _ => error));
}
