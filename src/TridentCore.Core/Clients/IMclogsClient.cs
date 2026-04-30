using Refit;
using TridentCore.Core.Models.MclogsApi;

namespace TridentCore.Core.Clients;

public interface IMclogsClient
{
    [Post("/1/log")]
    Task<CreateLogResponse> CreateLogAsync([Body] CreateLogRequest request);
}
