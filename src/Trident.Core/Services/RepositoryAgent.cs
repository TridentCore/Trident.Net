using System.Reflection;
using System.Text.Json;
using MessagePack;
using MessagePack.Resolvers;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Logging;
using Refit;
using Trident.Abstractions.Repositories;
using Trident.Abstractions.Repositories.Resources;
using Trident.Abstractions.Utilities;
using Trident.Core.Clients;
using Trident.Core.Repositories;
using Version = Trident.Abstractions.Repositories.Resources.Version;

namespace Trident.Core.Services;

public class RepositoryAgent
{
    private static readonly TimeSpan EXPIRED_IN = TimeSpan.FromDays(7);
    private static readonly string USER_AGENT = $"Polymerium/{Assembly.GetExecutingAssembly().GetName().Version}";

    private readonly MessagePackSerializerOptions _options =
        MessagePackSerializerOptions.Standard.WithResolver(ContractlessStandardResolver.Instance);

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
                case IRepositoryProviderAccessor.ProviderProfile.DriverType.CurseForge:
                {
                    var curseforge = new CurseForgeRepository(profile.Label,
                                                              RestService.For<ICurseForgeClient>(BuildClient(profile),
                                                                  new RefitSettings(new
                                                                      SystemTextJsonContentSerializer(new(JsonSerializerDefaults
                                                                         .Web)))));
                    built.Add(profile.Label, curseforge);
                    break;
                }
                case IRepositoryProviderAccessor.ProviderProfile.DriverType.Modrinth:
                {
                    var modrinth = new ModrinthRepository(profile.Label,
                                                          RestService.For<IModrinthClient>(BuildClient(profile),
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

    private IRepository Redirect(string label) =>
        _repositories.TryGetValue(label, out var repository)
            ? repository
            : throw new KeyNotFoundException($"{label} is not a listed repository label or not found");

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

    public Task<Package> IdentityAsync(string filePath)
    {
        // 如果文件不存在会返回 IOException
        // 如果文件过大导致 IO 异常或内存占用异常导致闪退是预期内事件
        var content = File.ReadAllBytes(filePath);
        return IdentityAsync(new ReadOnlyMemory<byte>(content));
    }

    public Task<Package> IdentityAsync(ReadOnlyMemory<byte> content)
    {
        // 不走缓存
        foreach (var (k, v) in _repositories)
        {
            try
            {
                return v.IdentifyAsync(content);
            }
            catch (ResourceNotFoundException) { }
        }

        throw new ResourceNotFoundException("No repository can identify the file");
    }

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

    public async Task<IReadOnlyList<Project>> QueryBatchAsync(IEnumerable<(string label, string? ns, string pid)> batch)
    {
        var batchArray = batch.ToArray();
        var cachedTasks = batchArray
                         .Select(async x => (Meta: x,
                                             Cached: await RetrieveCachedAsync<
                                                             Project>($"project:{PackageHelper.Identify(x.label, x.ns, x.pid, null, null)}")
                                                        .ConfigureAwait(false)))
                         .ToList();
        await Task.WhenAll(cachedTasks).ConfigureAwait(false);
        var cached = cachedTasks
                    .Where(x => x.IsCompletedSuccessfully && x.Result.Cached != null)
                    .Select(x => x.Result)
                    .ToList();

        var toQuery = batchArray.Except(cached.Select(x => x.Meta)).GroupBy(x => x.label);
        var queryTasks = toQuery
                        .Select(async x => (Label: x.Key,
                                            Projects: await Redirect(x.Key)
                                                           .QueryBatchAsync(x.Select(y => (y.ns, y.pid)))
                                                           .ConfigureAwait(false)))
                        .ToList();
        await Task.WhenAll(queryTasks).ConfigureAwait(false);
        var queried = queryTasks
                     .Select(x => x.Result)
                     .SelectMany(x => x.Projects.Select(y => (x.Label, Project: y)))
                     .ToList();
        foreach (var (label, project) in queried)
        {
            await
                CacheObjectAsync($"project:{PackageHelper.Identify(label, project.Namespace, project.ProjectId, null, null)}",
                                 project)
                   .ConfigureAwait(false);
        }

        return cached.Select(x => x.Cached!).Concat(queried.Select(x => x.Project)).ToList();
    }

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
            if (cacheEnabled)
            {
                await CacheObjectAsync(key, result).ConfigureAwait(false);
            }

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
        var cachedBytes = await _cache.GetAsync(key).ConfigureAwait(false);
        if (cachedBytes != null)
        {
            try
            {
                var cached = typeof(T) == typeof(byte[])
                                 ? (T)(object)cachedBytes
                                 : MessagePackSerializer.Deserialize<T>(cachedBytes, _options);
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

        return default;
    }

    private async Task CacheObjectAsync<T>(string key, T value)
    {
        await _cache
             .SetAsync(key,
                       value is not null
                           ? typeof(T) == typeof(byte[])
                                 ? (byte[])(object)value
                                 : MessagePackSerializer.Serialize(value, _options)
                           : [],
                       new() { SlidingExpiration = EXPIRED_IN })
             .ConfigureAwait(false);
        _logger.LogDebug("Cache recorded: {}", key);
    }

    #region Injected

    private readonly IDistributedCache _cache;
    private readonly ILogger<RepositoryAgent> _logger;
    private readonly IHttpClientFactory _clientFactory;

    #endregion
}
