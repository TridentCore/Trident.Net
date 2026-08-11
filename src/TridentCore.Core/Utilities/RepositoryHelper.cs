using TridentCore.Abstractions.Repositories;

namespace TridentCore.Core.Utilities;

// NOTE: 「扇出 id、并发执行、逐项捕获失败」模式的唯一宿主，各仓库批量操作共用；
//  取代原先散落在各仓库里的 tuple+WhenAll+try/catch 重复块。
public static class RepositoryHelper
{
    // NOTE: 对所有 id 并发执行解析；成功入 Successful，异常（OperationCanceledException 除外，
    //  直接上抛）按 id 归入 Failed——单条坏项不拖垮整批。
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

    // NOTE: 多步批处理的累加器——步骤 Succeed/Fail 写入，Merge 组合步骤，
    //  ToResult 最终展平为公开的 BatchResult 契约。
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

    // NOTE: 并发扇出与单线程聚合之间的逐项传输。
    public abstract record Outcome<TId, TItem> where TId : notnull
    {
        private Outcome() { }

        public sealed record Success(TId Id, TItem Item) : Outcome<TId, TItem>;

        public sealed record Failure(TId Id, Exception Error) : Outcome<TId, TItem>;
    }
}
