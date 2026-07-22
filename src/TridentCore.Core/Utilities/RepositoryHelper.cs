using TridentCore.Abstractions.Repositories;

namespace TridentCore.Core.Utilities;

// Single home for the "fan out ids, run each concurrently, trap per-item failures" pattern
// shared by every repository batch operation. Replaces the duplicated tuple+WhenAll+try/catch
// blocks that previously lived inside each repository.
public static class RepositoryHelper
{
    // Accumulator for a multi-step batch flow: steps Succeed/Fail into it, Merge composes steps,
    // and ToResolveResult flattens it into the public BatchResolveResult contract at the end.
    public sealed class BatchResult<TId, TItem> where TId : notnull
    {
        public Dictionary<TId, TItem> Successful { get; } = [];

        public Dictionary<TId, Exception> Failed { get; } = [];

        public BatchResult<TId, TItem> Succeed(TId id, TItem item)
        {
            Successful[id] = item;
            return this;
        }

        public BatchResult<TId, TItem> Fail(TId id, Exception error)
        {
            Failed[id] = error;
            return this;
        }

        public BatchResult<TId, TItem> FailAll(IEnumerable<TId> ids, Exception error)
        {
            foreach (var id in ids.Distinct())
            {
                Failed[id] = error;
            }

            return this;
        }

        public BatchResult<TId, TItem> Merge(BatchResult<TId, TItem> other)
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

        public BatchResolveResult<TId, TItem> ToResolveResult() => new(Successful, Failed);
    }

    // Runs resolve against every id concurrently; each success lands in Successful, each thrown
    // exception (other than OperationCanceledException, which propagates) is attributed per-id
    // into Failed so one bad entry never sinks the rest of the batch.
    public static async Task<BatchResult<TId, TItem>> ResolveAsync<TId, TItem>(
        IEnumerable<TId> ids,
        Func<TId, Task<TItem>> resolve)
        where TId : notnull
        where TItem : class
    {
        var result = new BatchResult<TId, TItem>();
        var outcomes = await Task
                             .WhenAll(ids.Select(async id =>
                              {
                                  try
                                  {
                                      return (Id: id,
                                          Item: (TItem?)await resolve(id).ConfigureAwait(false),
                                          Error: (Exception?)null);
                                  }
                                  catch (OperationCanceledException)
                                  {
                                      throw;
                                  }
                                  catch (Exception ex)
                                  {
                                      return (Id: id, Item: (TItem?)null, Error: ex);
                                  }
                              }))
                             .ConfigureAwait(false);

        foreach (var (id, item, error) in outcomes)
        {
            if (error is not null)
            {
                result.Fail(id, error);
            }
            else
            {
                result.Succeed(id, item!);
            }
        }

        return result;
    }
}
