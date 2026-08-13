using Refit;
using TridentCore.Core.Models.MclogsApi;

namespace TridentCore.Core.Clients;

public interface IMclogsClient
{
    [Post("/1/log")]
    Task<CreateLogResponse> CreateLogAsync([Body] CreateLogRequest request,
                                          CancellationToken cancellationToken = default);

    [Delete("/1/log/{id}")]
    Task DeleteLogAsync(string id, [Header("Authorization")] string authorization);
}
