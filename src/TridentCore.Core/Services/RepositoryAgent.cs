using System.Reflection;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Refit;
using TridentCore.Abstractions.Repositories;
using TridentCore.Abstractions.Repositories.Resources;
using TridentCore.Abstractions.Utilities;
using TridentCore.Core.Clients;
using TridentCore.Core.Repositories;
using TridentCore.Pref;
using Version = TridentCore.Abstractions.Repositories.Resources.Version;
using ZiggyCreatures.Caching.Fusion;

namespace TridentCore.Core.Services;

public class RepositoryAgent
{
    public const string CLIENT_NAME = "repository";

    private static readonly TimeSpan EXPIRED_IN = TimeSpan.FromDays(7);
    private static readonly string USER_AGENT =
        $"Trident.Net/{Assembly.GetExecutingAssembly().GetName().Version}";

    private readonly IReadOnlyDictionary<string, IRepository> _repositories;

    public RepositoryAgent(
        IEnumerable<IRepositoryProviderAccessor> accessors,
        ILogger<RepositoryAgent> logger,
        IFusionCache cache,
        IHttpClientFactory clientFactory
    )
    {
        _logger = logger;
        _cache = cache;
        _clientFactory = clientFactory;
        _repositories = BuildRepositories(accessors.ToList()).AsReadOnly();
    }

    public int Count => _repositories.Count;
    public IEnumerable<string> AllLabels => _repositories.Keys;
    public IEnumerable<string> Labels =>
        _repositories.Where(x => !x.Value.IsHidden).Select(x => x.Key);

    private IDictionary<string, IRepository> BuildRepositories(
        IReadOnlyList<IRepositoryProviderAccessor> accessors
    )
    {
        var built = new Dictionary<string, IRepository>();
        foreach (var profile in accessors.SelectMany(x => x.Build()))
        {
            switch (profile.Driver)
            {
                case IRepositoryProviderAccessor.ProviderProfile.DriverType.CurseForge:
                    {
                        var curseforge = new CurseForgeRepository(
                            profile.Label,
                            RestService.For<ICurseForgeClient>(
                                BuildClient(profile),
                                new RefitSettings(
                                    new SystemTextJsonContentSerializer(new(JsonSerializerDefaults.Web))
                                )
                                {
                                    UrlParameterFormatter = new LowercaseBoolUrlParameterFormatter(),
                                }
                            )
                        );
                        built.Add(profile.Label, curseforge);
                        break;
                    }
                case IRepositoryProviderAccessor.ProviderProfile.DriverType.Modrinth:
                    {
                        var modrinth = new ModrinthRepository(
                            profile.Label,
                            RestService.For<IModrinthClient>(
                                BuildClient(profile),
                                new RefitSettings(
                                    new SystemTextJsonContentSerializer(
                                        new(JsonSerializerDefaults.Web)
                                        {
                                            PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
                                        }
                                    )
                                )
                                {
                                    UrlParameterFormatter = new LowercaseBoolUrlParameterFormatter(),
                                }
                            )
                        );

                        built.Add(profile.Label, modrinth);
                        break;
                    }
                case IRepositoryProviderAccessor.ProviderProfile.DriverType.Packwiz:
                    {
                        var packwiz = new PackwizRepository(
                            profile.Label,
                            RestService.For<IGitHubClient>(
                                BuildClient(profile),
                                new RefitSettings(
                                    new SystemTextJsonContentSerializer(new(JsonSerializerDefaults.Web))
                                )
                            )
                        );
                        built.Add(profile.Label, packwiz);
                        break;
                    }
            }
        }

        foreach (var custom in accessors.SelectMany(x => x.BuildCustom()))
        {
            built.Add(custom.Label, custom.Instance);
        }

        return built;
    }

    private HttpClient BuildClient(IRepositoryProviderAccessor.ProviderProfile profile)
    {
        var client = _clientFactory.CreateClient(CLIENT_NAME);
        client.BaseAddress = new(profile.Endpoint);
        if (
            !(
                profile.UserAgent is not null
                && client.DefaultRequestHeaders.UserAgent.TryParseAdd(new(profile.UserAgent))
            )
        )
        {
            client.DefaultRequestHeaders.UserAgent.TryParseAdd(new(USER_AGENT));
        }

        return client;
    }

    private IRepository Redirect(string label) =>
        _repositories.TryGetValue(label, out var repository)
            ? repository
            : throw new KeyNotFoundException(
                $"{label} is not a listed repository label or not found"
            );

    public Task<RepositoryStatus> CheckStatusAsync(string label) =>
        RetrieveCachedAsync($"status:{label}", () => Redirect(label).CheckStatusAsync());

    public Task<IPaginationHandle<Exhibit>> SearchAsync(
        string label,
        string query,
        Filter filter
    ) => Redirect(label).SearchAsync(query, filter);

    public Task<Package> ResolveAsync(
        PackageIdentifier id,
        Filter filter,
        bool cacheEnabled = true
    ) =>
        RetrieveCachedAsync(
            $"package:{PackageHelper.Identify(id.Repository, id.Namespace, id.Identity, id.Version, filter)}",
            () => Redirect(id.Repository).ResolveAsync(id.ToScoped(), filter),
            cacheEnabled
        );

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

    public async Task<BatchResolveResult<PackageIdentifier, Package>> ResolveBatchAsync(
        IEnumerable<PackageIdentifier> batch,
        Filter filter
    )
    {
        var batchArray = batch.ToArray();
        var cached = await RetrieveCachedBatchAsync<PackageIdentifier, Package>(
                batchArray,
                x =>
                    $"package:{PackageHelper.Identify(x.Repository, x.Namespace, x.Identity, x.Version, filter)}"
            )
            .ConfigureAwait(false);

        var successful = cached.ToDictionary(x => x.Item, x => x.Cached);
        var failed = new Dictionary<PackageIdentifier, Exception>();

        var toResolve = batchArray.Except(cached.Select(x => x.Item)).GroupBy(x => x.Repository);
        var resolveTasks = toResolve.Select(async group =>
        {
            var items = group.ToArray();
            try
            {
                var result = await Redirect(group.Key)
                    .ResolveBatchAsync(items.Select(PackageIdentifierExtensions.ToScoped), filter)
                    .ConfigureAwait(false);
                return result.MapKeys(x => x.ToUnscoped(group.Key));
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                return BatchResolveResult<PackageIdentifier, Package>.FromFailures(items, ex);
            }
        });

        foreach (var result in await Task.WhenAll(resolveTasks).ConfigureAwait(false))
        {
            foreach (var (pref, package) in result.Successful)
            {
                successful[pref] = package;
                await CacheObjectAsync(
                        $"package:{PackageHelper.Identify(pref.Repository, pref.Namespace, pref.Identity, pref.Version, filter)}",
                        package
                    )
                    .ConfigureAwait(false);
            }

            foreach (var (pref, error) in result.Failed)
                failed[pref] = error;
        }

        return new(successful, failed);
    }


    public Task<Project> QueryAsync(ProjectIdentifier id) =>
        RetrieveCachedAsync(
            $"project:{PackageHelper.Identify(id.Repository, id.Namespace, id.Identity, null, null)}",
            () => Redirect(id.Repository).QueryAsync(id.ToScoped())
        );

    public async Task<BatchResolveResult<ProjectIdentifier, Project>> QueryBatchAsync(
        IEnumerable<ProjectIdentifier> batch
    )
    {
        var batchArray = batch.ToArray();
        var cached = await RetrieveCachedBatchAsync<
            ProjectIdentifier,
            Project
        >(batchArray, x => $"project:{PackageHelper.Identify(x.Repository, x.Namespace, x.Identity, null, null)}")
            .ConfigureAwait(false);

        var successful = cached.ToDictionary(x => x.Item, x => x.Cached);
        var failed = new Dictionary<ProjectIdentifier, Exception>();

        var toQuery = batchArray.Except(cached.Select(x => x.Item)).GroupBy(x => x.Repository);
        var queryTasks = toQuery.Select(async group =>
        {
            var items = group.ToArray();
            try
            {
                var result = await Redirect(group.Key)
                    .QueryBatchAsync(items.Select(ProjectIdentifierExtensions.ToScoped))
                    .ConfigureAwait(false);
                return result.MapKeys(x => x.ToUnscoped(group.Key));
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                return BatchResolveResult<ProjectIdentifier, Project>.FromFailures(items, ex);
            }
        });

        foreach (var result in await Task.WhenAll(queryTasks).ConfigureAwait(false))
        {
            foreach (var (key, project) in result.Successful)
            {
                successful[key] = project;
                await CacheObjectAsync(
                        $"project:{PackageHelper.Identify(key.Repository, key.Namespace, key.Identity, null, null)}",
                        project
                    )
                    .ConfigureAwait(false);
            }

            foreach (var (key, error) in result.Failed)
                failed[key] = error;
        }

        return new(successful, failed);
    }


    public Task<string> ReadDescriptionAsync(ProjectIdentifier id) =>
        RetrieveCachedAsync(
            $"description:{PackageHelper.Identify(id.Repository, id.Namespace, id.Identity, null, null)}",
            () => Redirect(id.Repository).ReadDescriptionAsync(id.ToScoped())
        );

    public Task<string> ReadChangelogAsync(PackageIdentifier id) =>
        RetrieveCachedAsync(
            $"changelog:{PackageHelper.Identify(id.Repository, id.Namespace, id.Identity, id.Version, null)}",
            () => Redirect(id.Repository).ReadChangelogAsync(id.ToScoped())
        );

    public Task<IPaginationHandle<Version>> InspectAsync(ProjectIdentifier id, Filter filter) =>
        Redirect(id.Repository).InspectAsync(id.ToScoped(), filter);

    private async Task<T> RetrieveCachedAsync<T>(
        string key,
        Func<Task<T>> factory,
        bool cacheEnabled = true
    )
    {
        if (!cacheEnabled)
        {
            return await factory().ConfigureAwait(false);
        }

        try
        {
            return await _cache
                .GetOrSetAsync(key, _ => factory(), EXPIRED_IN)
                .ConfigureAwait(false);
        }
        catch (Exception e)
        {
            _logger.LogError(e, "Exception occurred: {message}", e.Message);
            throw;
        }
    }

    private async ValueTask<T?> RetrieveCachedAsync<T>(string key)
    {
        var cached = await _cache.TryGetAsync<T>(key).ConfigureAwait(false);
        if (cached.HasValue)
        {
            _logger.LogDebug("Cache hit: {}", key);
            return cached.Value;
        }

        return default;
    }

    private async Task<List<(TItem Item, TValue Cached)>> RetrieveCachedBatchAsync<TItem, TValue>(
        IReadOnlyList<TItem> batch,
        Func<TItem, string> keySelector
    )
        where TValue : class
    {
        var cachedTasks = batch
            .Select(async x =>
                (
                    Item: x,
                    Cached: await RetrieveCachedAsync<TValue>(keySelector(x)).ConfigureAwait(false)
                )
            )
            .ToList();
        await Task.WhenAll(cachedTasks).ConfigureAwait(false);

        return cachedTasks
            .Where(x => x.IsCompletedSuccessfully && x.Result.Cached != null)
            .Select(x => (x.Result.Item, x.Result.Cached!))
            .ToList();
    }

    private async Task CacheObjectAsync<T>(string key, T value)
    {
        await _cache.SetAsync(key, value, EXPIRED_IN).ConfigureAwait(false);
        _logger.LogDebug("Cache recorded: {}", key);
    }

    #region Injected

    private readonly IFusionCache _cache;
    private readonly ILogger<RepositoryAgent> _logger;
    private readonly IHttpClientFactory _clientFactory;

    #endregion
}
