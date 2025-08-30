using System.Diagnostics;
using System.Reflection;
using System.Text.Json;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Logging;
using Trident.Core.Clients;
using Trident.Core.Repositories;
using Refit;
using Trident.Abstractions.Repositories;
using Trident.Abstractions.Repositories.Resources;
using Trident.Abstractions.Utilities;
using Version = Trident.Abstractions.Repositories.Resources.Version;

namespace Trident.Core.Services
{
    public class RepositoryAgent
    {
        private static readonly TimeSpan EXPIRED_IN = TimeSpan.FromDays(7);
        private static readonly string USER_AGENT = $"Polymerium/{Assembly.GetExecutingAssembly().GetName().Version}";

        private readonly IReadOnlyDictionary<string, IRepository> _repositories;

        public RepositoryAgent(
            IEnumerable<IRepositoryProviderAccessor> accessors,
            ILogger<RepositoryAgent> logger,
            IDistributedCache cache,
            IHttpClientFactory clientFactory)
        {
            _logger = logger;
            _cache = cache;
            _clientFactory = clientFactory;
            _repositories = BuildRepositories(accessors).AsReadOnly();
        }

        public int Count => _repositories.Count;
        public IEnumerable<string> Labels => _repositories.Keys;

        private IDictionary<string, IRepository> BuildRepositories(IEnumerable<IRepositoryProviderAccessor> accessors)
        {
            var built = new Dictionary<string, IRepository>();
            foreach (var profile in accessors.SelectMany(x => x.Build()))
            {
                switch (profile.Driver)
                {
                    case IRepositoryProviderAccessor.ProviderProfile.DriverType.Modrinth:
                        {
                            var modrinth = new ModrinthRepository(RestService.For<IModrinthClient>(BuildClient(profile),
                                                                      new RefitSettings(new
                                                                          SystemTextJsonContentSerializer(new(JsonSerializerDefaults
                                                                             .Web)
                                                                          {
                                                                              PropertyNamingPolicy =
                                                                                  JsonNamingPolicy
                                                                                     .SnakeCaseLower
                                                                          }))));

                            built.Add(profile.Label, modrinth);
                            break;
                        }
                    case IRepositoryProviderAccessor.ProviderProfile.DriverType.CurseForge:
                        {
                            var curseforge =
                                new CurseForgeRepository(RestService.For<ICurseForgeClient>(BuildClient(profile),
                                                             new RefitSettings(new
                                                                                   SystemTextJsonContentSerializer(new(JsonSerializerDefaults
                                                                                      .Web)))));
                            built.Add(profile.Label, curseforge);
                            break;
                        }
                }
            }

            return built;
        }

        private HttpClient BuildClient(IRepositoryProviderAccessor.ProviderProfile profile)
        {
            var client = _clientFactory.CreateClient();
            client.BaseAddress = new(profile.Endpoint);
            if (profile.UserAgent is not null)
            {
                client.DefaultRequestHeaders.UserAgent.Add(new(USER_AGENT));
            }

            if (profile.AuthorizationHeader is { Key: { } key, Value: { } value })
            {
                client.DefaultRequestHeaders.Add(key, value);
            }

            return client;
        }

        private IRepository Redirect(string label)
        {
            if (_repositories.TryGetValue(label, out var repository))
            {
                return repository;
            }

            throw new KeyNotFoundException($"{label} is not a listed repository label or not found");
        }

        public Task<RepositoryStatus> CheckStatusAsync(string label) =>
            RetrieveCachedAsync($"status:{label}", () => Redirect(label).CheckStatusAsync());

        public Task<IPaginationHandle<Exhibit>> SearchAsync(string label, string query, Filter filter) =>
            Redirect(label).SearchAsync(query, filter);

        public Task<Package> ResolveAsync(
            string label,
            string? ns,
            string pid,
            string? vid,
            Filter filter,
            bool cacheEnabled = true) =>
            RetrieveCachedAsync($"package:{PackageHelper.Identify(label, ns, pid, vid, filter)}",
                                () => Redirect(label).ResolveAsync(ns, pid, vid, filter),
                                cacheEnabled);

        public async Task<IReadOnlyList<Package>> ResolveBatchAsync(
            IEnumerable<(string label, string? ns, string pid, string? vid)> batch,
            Filter filter)
        {
            var batchArray = batch.ToArray();
            var cachedTasks = batchArray
                             .Select(async x => (Meta: x,
                                                 Cached: await RetrieveCachedAsync<
                                                                 Package>($"package:{PackageHelper.Identify(x.label, x.ns, x.pid, x.vid, filter)}")
                                                            .ConfigureAwait(false)))
                             .ToList();
            await Task.WhenAll(cachedTasks).ConfigureAwait(false);
            var cached = cachedTasks
                        .Where(x => x.IsCompletedSuccessfully && x.Result.Cached != null)
                        .Select(x => x.Result)
                        .ToList();

            var toResolve = batchArray.Except(cached.Select(x => x.Meta)).GroupBy(x => x.label);
            var resolveTasks = toResolve
                              .Select(async x => (Label: x.Key,
                                                  Packages: await Redirect(x.Key)
                                                                 .ResolveBatchAsync(x.Select(y => (y.ns, y.pid, y.vid)),
                                                                      filter)
                                                                 .ConfigureAwait(false)))
                              .ToList();
            await Task.WhenAll(resolveTasks).ConfigureAwait(false);
            var resolved = resolveTasks
                          .Where(x => x.IsCompletedSuccessfully && x.Result != default)
                          .Select(x => x.Result)
                          .SelectMany(x => x.Packages.Select(y => (x.Label, Package: y)))
                          .ToList();
            foreach (var (label, package) in resolved)
            {
                await
                    CacheObjectAsync($"package:{PackageHelper.Identify(label, package.Namespace, package.ProjectId, package.VersionId, filter)}",
                                     package)
                       .ConfigureAwait(false);
            }

            return cached.Select(x => x.Cached!).Concat(resolved.Select(x => x.Package)).ToList();
        }

        public Task<Project> QueryAsync(string label, string? ns, string pid) =>
            RetrieveCachedAsync($"project:{PackageHelper.Identify(label, ns, pid, null, null)}",
                                () => Redirect(label).QueryAsync(ns, pid));

        public Task<Project> QueryBatchAsync(IEnumerable<(string, string?, string)> batch) =>
            throw new NotImplementedException();

        public Task<string> ReadDescriptionAsync(string label, string? ns, string pid) =>
            RetrieveCachedAsync($"description:{PackageHelper.Identify(label, ns, pid, null, null)}",
                                () => Redirect(label).ReadDescriptionAsync(ns, pid));

        public Task<string> ReadChangelogAsync(string label, string? ns, string pid, string vid) =>
            RetrieveCachedAsync($"Changelog:{PackageHelper.Identify(label, ns, pid, vid, null)}",
                                () => Redirect(label).ReadChangelogAsync(ns, pid, vid));

        public Task<IPaginationHandle<Version>> InspectAsync(string label, string? ns, string pid, Filter filter) =>
            Redirect(label).InspectAsync(ns, pid, filter);

        public Task<byte[]> SeeAsync(Uri url) =>
            RetrieveCachedAsync($"thumbnail:{url}",
                                async () =>
                                {
                                    using var client = _clientFactory.CreateClient();
                                    return await client.GetByteArrayAsync(url).ConfigureAwait(false);
                                });

        private async Task<T> RetrieveCachedAsync<T>(string key, Func<Task<T>> factory, bool cacheEnabled = true)
        {
            var cached = cacheEnabled ? await RetrieveCachedAsync<T>(key).ConfigureAwait(false) : default;
            if (cached != null)
            {
                return cached;
            }

            try
            {
                var result = await factory().ConfigureAwait(false);
                await CacheObjectAsync(key, result).ConfigureAwait(false);
                return result;
            }
            catch (Exception e)
            {
                _logger.LogError(e, "Exception occurred: {message}", e.Message);
                throw;
            }
        }

        private async ValueTask<T?> RetrieveCachedAsync<T>(string key)
        {
            var cachedJson = await _cache.GetStringAsync(key).ConfigureAwait(false);
            if (cachedJson != null)
            {
                try
                {
                    var cached = JsonSerializer.Deserialize<T>(cachedJson);
                    _logger.LogDebug("Cache hit: {}", key);
                    // await _cache.RefreshAsync(key).ConfigureAwait(false);
                    // NOTE: 不刷新！过期就让他过期，因为是由时效性的
                    //  刷新！因为当前包的解析版本落后无伤大雅，而版本列表是无持久缓存的，不会导致时效性问题
                    return cached;
                }
                catch (Exception e)
                {
                    _logger.LogError(e, "Broken cache hit: {}", key);
                }
            }

            _logger.LogDebug("Cache missed: {}", key);
            return default;
        }

        private async Task CacheObjectAsync<T>(string key, T value)
        {
            await _cache
                 .SetStringAsync(key,
                                 JsonSerializer.Serialize(value),
                                 new DistributedCacheEntryOptions { SlidingExpiration = EXPIRED_IN })
                 .ConfigureAwait(false);
            _logger.LogDebug("Cache recorded: {}", key);
        }

        #region Injected

        private readonly IDistributedCache _cache;
        private readonly ILogger<RepositoryAgent> _logger;
        private readonly IHttpClientFactory _clientFactory;

        #endregion
    }
}
