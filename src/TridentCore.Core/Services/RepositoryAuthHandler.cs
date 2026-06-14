using System.Linq;
using System.Net.Http;

namespace TridentCore.Core.Services;

public class RepositoryAuthHandler : DelegatingHandler
{
    private static Dictionary<string, (string Key, string Value)>? _authMap;
    private static readonly Lock AUTH_MAP_LOCK = new();

    public RepositoryAuthHandler(IEnumerable<IRepositoryProviderAccessor> accessors)
    {
        if (_authMap is null)
        {
            lock (AUTH_MAP_LOCK)
            {
                _authMap ??= accessors
            .SelectMany(a => a.Build())
            .Where(p => p.AuthorizationHeader is { Key: not null, Value: not null } auth)
            .SelectMany(p =>
            {
                var auth = p.AuthorizationHeader!.Value;
                var entries = new List<(string Host, (string Key, string Value) Auth)>
                {
                    // Always include the API endpoint host
                    (new Uri(p.Endpoint).Host, auth)
                };

                // Include explicitly declared CDN hosts
                if (p.CdnHosts is { Count: > 0 })
                {
                    foreach (var host in p.CdnHosts)
                    {
                        entries.Add((host, auth));
                    }
                }

                return entries;
            })
            .DistinctBy(x => x.Host)
            .ToDictionary(x => x.Host, x => x.Auth, StringComparer.OrdinalIgnoreCase);
            }
        }
    }

    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken
    )
    {
        if (request.RequestUri is { } uri && _authMap!.TryGetValue(uri.Host, out var auth))
        {
            request.Headers.TryAddWithoutValidation(auth.Key, auth.Value);
        }

        return await base.SendAsync(request, cancellationToken).ConfigureAwait(false);
    }
}
