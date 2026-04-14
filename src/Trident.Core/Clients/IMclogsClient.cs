using Refit;
using Trident.Core.Models.MclogsApi;

namespace Trident.Core.Clients;

public interface IMclogsClient
{
    [Post("/1/log")]
    Task<CreateLogResponse> CreateLogAsync([Body] CreateLogRequest request);
}
