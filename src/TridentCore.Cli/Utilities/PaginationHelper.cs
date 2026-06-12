using System.Runtime.CompilerServices;
using TridentCore.Abstractions.Repositories;

namespace TridentCore.Cli.Utilities;

public static class PaginationHelper
{
    public static async IAsyncEnumerable<T> FetchWindowAsync<T>(
        IPaginationHandle<T> handle,
        int index,
        int limit,
        [EnumeratorCancellation] CancellationToken cancellationToken
    )
    {
        if (limit <= 0 || index < 0 || (ulong)index >= handle.TotalCount)
        {
            yield break;
        }

        var startPage = (uint)(index / (int)handle.PageSize);
        var skipInPage = index % (int)handle.PageSize;
        var yielded = 0;
        var isFirstPage = true;

        for (var page = startPage; yielded < limit; page++)
        {
            handle.PageIndex = page;
            var batch = (
                await handle.FetchAsync(cancellationToken).ConfigureAwait(false)
            ).ToArray();
            if (batch.Length == 0)
            {
                yield break;
            }

            var localStart = isFirstPage ? skipInPage : 0;
            isFirstPage = false;

            for (var i = localStart; i < batch.Length; i++)
            {
                yield return batch[i];
                if (++yielded >= limit)
                {
                    yield break;
                }
            }
        }
    }
}
