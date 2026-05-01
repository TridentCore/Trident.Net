using System.Runtime.CompilerServices;
using TridentCore.Abstractions.Repositories;

namespace TridentCore.Cli.Services;

public static class PaginationHelper
{
    public static async IAsyncEnumerable<T> FetchWindowAsync<T>(
        IPaginationHandle<T> handle,
        int index,
        int limit,
        [EnumeratorCancellation] CancellationToken cancellationToken
    )
    {
        var skipped = 0;
        var yielded = 0;
        var page = 0u;
        while (yielded < limit && skipped < index + limit && skipped < (int)handle.TotalCount)
        {
            handle.PageIndex = page++;
            var batch = (
                await handle.FetchAsync(cancellationToken).ConfigureAwait(false)
            ).ToArray();
            if (batch.Length == 0)
            {
                yield break;
            }

            foreach (var item in batch)
            {
                if (skipped++ < index)
                {
                    continue;
                }

                yield return item;
                if (++yielded >= limit)
                {
                    yield break;
                }
            }
        }
    }
}
