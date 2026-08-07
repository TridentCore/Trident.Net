using TridentCore.Abstractions.Repositories;

namespace TridentCore.Core.Utilities;

// Single home for the "fan out ids, run each concurrently, trap per-item failures" pattern
// shared by every repository batch operation. Replaces the duplicated tuple+WhenAll+try/catch
// blocks that previously lived inside each repository.
public static class RepositoryHelper
{
    // Runs resolve against every id concurrently; each success lands in Successful, each thrown
    // exception (other than OperationCanceledException, which propagates) is attributed per-id
    // into Failed so one bad entry never sinks the rest of the batch.
    public static async Task<BatchResultBuilder<TId, TItem>> ResolveAsync<TId, TItem>(
        IEnumerable<TId> ids,
        Func<TId, Task<TItem>> resolve) where TId : notnull where TItem : class
    {
        var result = new BatchResultBuilder<TId, TItem>();
        var outcomes = await Task
                            .WhenAll(ids.Select(async id =>
                             {
                                 try
                                 {
                                     return (Outcome<TId, TItem>)new Outcome<TId, TItem>.Success(
                                         id, await resolve(id).ConfigureAwait(false));
                                 }
                                 catch (OperationCanceledException)
                                 {
                                     throw;
                                 }
                                 catch (Exception ex)
                                 {
                                     return new Outcome<TId, TItem>.Failure(id, ex);
                                 }
                             }))
                            .ConfigureAwait(false);

        foreach (var outcome in outcomes)
        {
            switch (outcome)
            {
                case Outcome<TId, TItem>.Success s:
                    result.Succeed(s.Id, s.Item);
                    break;
                case Outcome<TId, TItem>.Failure f:
                    result.Fail(f.Id, f.Error);
                    break;
            }
        }

        return result;
    }

    // Accumulator for a multi-step batch flow: steps Succeed/Fail into it, Merge composes steps,
    // and ToResult flattens it into the public BatchResult contract at the end.
    public sealed class BatchResultBuilder<TId, TItem> where TId : notnull
    {
        public Dictionary<TId, TItem> Successful { get; } = [];

        public Dictionary<TId, Exception> Failed { get; } = [];

        public BatchResultBuilder<TId, TItem> Succeed(TId id, TItem item)
        {
            Successful[id] = item;
            return this;
        }

        public BatchResultBuilder<TId, TItem> Fail(TId id, Exception error)
        {
            Failed[id] = error;
            return this;
        }

        public BatchResultBuilder<TId, TItem> FailAll(IEnumerable<TId> ids, Exception error)
        {
            foreach (var id in ids.Distinct())
            {
                Failed[id] = error;
            }

            return this;
        }

        public BatchResultBuilder<TId, TItem> Merge(BatchResultBuilder<TId, TItem> other)
        {
            foreach (var (key, value) in other.Successful)
            {
                Successful[key] = value;
            }

            foreach (var (key, value) in other.Failed)
            {
                Failed[key] = value;
            }

            return this;
        }

        public BatchResult<TId, TItem> ToResult() => new(Successful, Failed);
    }

    // Per-item transport between the concurrent fan-out and the single-threaded aggregation.
    public abstract record Outcome<TId, TItem> where TId : notnull
    {
        private Outcome() { }

        public sealed record Success(TId Id, TItem Item) : Outcome<TId, TItem>;

        public sealed record Failure(TId Id, Exception Error) : Outcome<TId, TItem>;
    }
}
