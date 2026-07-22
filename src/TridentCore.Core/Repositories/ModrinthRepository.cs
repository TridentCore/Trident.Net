using System.Net;
using System.Security.Cryptography;
using System.Text.Json;
using Refit;
using TridentCore.Abstractions.Repositories;
using TridentCore.Abstractions.Repositories.Resources;
using TridentCore.Core.Clients;
using TridentCore.Core.Models.ModrinthApi;
using TridentCore.Core.Utilities;
using TridentCore.Pref;
using Version = TridentCore.Abstractions.Repositories.Resources.Version;

namespace TridentCore.Core.Repositories;

public class ModrinthRepository(string label, IModrinthClient client) : IRepository
{
    private const uint PAGE_SIZE = 20;

    private static string ArrayParameterConstructor(IEnumerable<string?> members) =>
        JsonSerializer.Serialize(members.Where(x => x is not null).ToArray());

    // v3 把 game_versions 等过滤并进了 loader_fields（JSON 对象 {"game_versions":[...]}），
    // 没有独立的 game_versions 参数（v2 才有，容易踩坑）
    private static string? BuildLoaderFields(params (string Key, string? Value)[] fields)
    {
        var dict = fields.Where(f => f.Value is not null).ToDictionary(f => f.Key, f => new[] { f.Value });
        return dict.Count == 0 ? null : JsonSerializer.Serialize(dict);
    }

    private static string FormatTarget(Filter filter) => $"{filter.Version ?? "*"}/{filter.Loader ?? "*"}";

    #region IRepository Members

    public async Task<RepositoryStatus> CheckStatusAsync()
    {
        var (loadersTask, versionsTask, typesTask) = (client.GetLoadersAsync(), client.GetGameVersionsAsync(),
                                                      client.GetProjectTypesAsync());
        var (loaders, versions, types) = (ModrinthHelper.ToLoaderNames(await loadersTask.ConfigureAwait(false)),
                                          ModrinthHelper.ToVersionNames(await versionsTask.ConfigureAwait(false)),
                                          await typesTask.ConfigureAwait(false));
        var supportedLoaders = loaders
                              .Select(x => ModrinthHelper.ModloaderMappings.GetValueOrDefault(x))
                              .Where(x => x != null)
                              .Select(x => x!)
                              .ToList();
        var supportedKinds = types
                            .Select(ModrinthHelper.ProjectTypeToKind)
                            .Where(x => x != null)
                            .Select(x => x!.Value)
                            .ToList();
        return new(supportedLoaders, versions.ToList(), supportedKinds);
    }

    public async Task<IPaginationHandle<Exhibit>> SearchAsync(string query, Filter filter)
    {
        var loader = filter.Kind is ResourceKind.Mod or ResourceKind.Modpack
                         ? ModrinthHelper.LoaderIdToName(filter.Loader)
                         : null;
        var first = await client
                         .SearchAsync(query,
                                      ModrinthHelper.BuildFacets(ModrinthHelper.ResourceKindToType(filter.Kind),
                                                                 filter.Version,
                                                                 loader),
                                      limit: PAGE_SIZE)
                         .ConfigureAwait(false);
        var initial = first.Hits.Select(x => ModrinthHelper.ToExhibit(label, x));
        return new PaginationHandle<Exhibit>(initial,
                                             first.Limit,
                                             first.TotalHits,
                                             async (pageIndex, _) =>
                                             {
                                                 var rv = await client
                                                               .SearchAsync(query,
                                                                            ModrinthHelper.BuildFacets(ModrinthHelper
                                                                                   .ResourceKindToType(filter
                                                                                       .Kind),
                                                                                filter.Version,
                                                                                loader),
                                                                            offset: pageIndex * first.Limit,
                                                                            limit: first.Limit)
                                                               .ConfigureAwait(false);
                                                 var exhibits = rv
                                                               .Hits.Select(x => ModrinthHelper.ToExhibit(label, x))
                                                               .ToList();
                                                 return exhibits;
                                             });
    }

    public async Task<Package> IdentifyAsync(ReadOnlyMemory<byte> content)
    {
        var hash = Convert.ToHexString(SHA1.HashData(content.Span));
        var info = await client.GetVersionFromHashAsync(hash).ConfigureAwait(false);
        var project = await client.GetProjectAsync(info.ProjectId).ConfigureAwait(false);
        var members = await client.GetTeamMembersAsync(project.TeamId).ConfigureAwait(false);
        return ModrinthHelper.ToPackage(label, project, info, members.FirstOrDefault());
    }

    public async Task<PackageIdentifier> RecognizeAsync(Uri uri, CancellationToken cancellationToken = default)
    {
        if (!uri.Host.EndsWith("modrinth.com", StringComparison.OrdinalIgnoreCase))
        {
            throw new ResourceNotFoundException($"{uri} is not a modrinth URL");
        }

        var (slug, version) = ExtractReference(uri);
        if (string.IsNullOrEmpty(slug))
        {
            throw new ResourceNotFoundException($"{uri} has no project slug");
        }

        try
        {
            var project = await client.GetProjectAsync(slug).ConfigureAwait(false);
            return new(label, null, project.Id, version);
        }
        catch (ApiException ex) when (ex.StatusCode == HttpStatusCode.NotFound)
        {
            throw new ResourceNotFoundException($"{slug} not found in the repository");
        }
    }

    // modrinth.com/{type}/{slug} and modrinth.com/{type}/{slug}/version/{versionId}
    private static (string? Slug, string? Version) ExtractReference(Uri uri)
    {
        var segments = uri.AbsolutePath.Split('/', StringSplitOptions.RemoveEmptyEntries);
        for (var i = 0; i < segments.Length; i++)
        {
            if (segments[i] == "version" && i > 0 && i + 1 < segments.Length)
            {
                return (segments[i - 1], segments[i + 1]);
            }
        }

        return (segments.LastOrDefault(), null);
    }

    public async Task<Project> QueryAsync(ScopedProjectIdentifier id)
    {
        var project = await client.GetProjectAsync(id.Identity).ConfigureAwait(false);
        var members = await client.GetTeamMembersAsync(project.TeamId).ConfigureAwait(false);
        return ModrinthHelper.ToProject(label, project, members.FirstOrDefault());
    }

    public async Task<BatchResolveResult<ScopedProjectIdentifier, Project>> QueryBatchAsync(
        IEnumerable<ScopedProjectIdentifier> batch)
    {
        var ids = batch.ToArray();
        var result = new RepositoryHelper.BatchResult<ScopedProjectIdentifier, Project>();

        Dictionary<string, ProjectInfo> projects;
        try
        {
            projects = (await client
                             .GetMultipleProjectsAsync(ArrayParameterConstructor(ids.Select(x => x.Identity)))
                             .ConfigureAwait(false))
                       .ToDictionary(x => x.Id);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            return result.FailAll(ids, ex).ToResolveResult();
        }

        var members = await PrefetchMembersAsync(projects.Values).ConfigureAwait(false);

        foreach (var id in ids)
        {
            if (!projects.TryGetValue(id.Identity, out var project))
            {
                result.Fail(id, new ResourceNotFoundException($"{id.Identity} not found in the repository"));
                continue;
            }

            if (members.Failed.TryGetValue(project.Id, out var memberError))
            {
                result.Fail(id, memberError);
                continue;
            }

            result.Succeed(id, ModrinthHelper.ToProject(label, project, members.Successful[project.Id]));
        }

        return result.ToResolveResult();
    }

    public async Task<Package> ResolveAsync(ScopedPackageIdentifier id, Filter filter)
    {
        try
        {
            var project = await client.GetProjectAsync(id.Identity).ConfigureAwait(false);
            if (id.Version != null)
            {
                var (versionTask, membersTask) = (client.GetVersionAsync(id.Version).ConfigureAwait(false),
                                                  client.GetTeamMembersAsync(project.TeamId).ConfigureAwait(false));
                var (version, members) = (await versionTask, await membersTask);
                return ModrinthHelper.ToPackage(label, project, version, members.FirstOrDefault());
            }
            else
            {
                var loader =
                    ModrinthHelper.GetVersionLoaderFilter(project.ProjectTypes.FirstOrDefault(), filter.Loader);
                var (versionsTask, membersTask) = (client
                                                  .GetProjectVersionsAsync(id.Identity,
                                                                           null,
                                                                           loader is not null
                                                                               ? ArrayParameterConstructor([loader])
                                                                               : null,
                                                                           BuildLoaderFields(("game_versions",
                                                                                           filter.Version)),
                                                                           limit: 1)
                                                  .ConfigureAwait(false),
                                                   client.GetTeamMembersAsync(project.TeamId).ConfigureAwait(false));
                var (versions, members) = (await versionsTask, await membersTask);
                var found = versions.OrderByDescending(x => x.DatePublished).FirstOrDefault();
                if (found == null)
                {
                    throw new
                        ResourceNotFoundException($"{project.Name} ({label}:{id.Identity}@*) has no matched version for {FormatTarget(filter)}");
                }

                return ModrinthHelper.ToPackage(label, project, found, members.FirstOrDefault());
            }
        }
        catch (ApiException ex)
        {
            if (ex.StatusCode == HttpStatusCode.NotFound)
            {
                throw new ResourceNotFoundException($"{id.Identity}/{id.Version ?? "*"} not found in the repository");
            }

            throw;
        }
    }

    public async Task<BatchResolveResult<ScopedPackageIdentifier, Package>> ResolveBatchAsync(
        IEnumerable<ScopedPackageIdentifier> batch,
        Filter filter)
    {
        var ids = batch.ToArray();
        var knownVids = ids.Where(x => x.Version is not null).ToArray();
        var unknownVids = ids.Where(x => x.Version is null).ToArray();
        var result = new RepositoryHelper.BatchResult<ScopedPackageIdentifier, Package>();

        Dictionary<string, ProjectInfo> projects;
        try
        {
            projects = (await client
                             .GetMultipleProjectsAsync(ArrayParameterConstructor(ids.Select(x => x.Identity)))
                             .ConfigureAwait(false))
                       .ToDictionary(x => x.Id);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            return result.FailAll(ids, ex).ToResolveResult();
        }

        var members = await PrefetchMembersAsync(projects.Values).ConfigureAwait(false);

        if (unknownVids.Length > 0)
        {
            result.Merge(await RepositoryHelper
                             .ResolveAsync(unknownVids,
                                 id => ResolveUnknownVersionAsync(id, filter, projects, members))
                             .ConfigureAwait(false));
        }

        if (knownVids.Length > 0)
        {
            result.Merge(await ResolveKnownVersionsAsync(knownVids, projects, members).ConfigureAwait(false));
        }

        return result.ToResolveResult();
    }

    private Task<RepositoryHelper.BatchResult<string, MemberInfo>> PrefetchMembersAsync(
        IEnumerable<ProjectInfo> projects)
    {
        var byId = projects.ToDictionary(p => p.Id);
        return RepositoryHelper.ResolveAsync(byId.Keys, async projectId =>
        {
            var project = byId[projectId];
            var member = (await client.GetTeamMembersAsync(project.TeamId).ConfigureAwait(false)).FirstOrDefault()
                      ?? throw new ResourceNotFoundException(
                             $"{project.Name} ({label}:{project.Id}) has no team member in the repository");
            return member;
        });
    }

    private async Task<Package> ResolveUnknownVersionAsync(
        ScopedPackageIdentifier id,
        Filter filter,
        IReadOnlyDictionary<string, ProjectInfo> projects,
        RepositoryHelper.BatchResult<string, MemberInfo> members)
    {
        var project = projects.GetValueOrDefault(id.Identity)
                    ?? throw new ResourceNotFoundException($"{id.Identity} not found in the repository");
        if (members.Failed.TryGetValue(project.Id, out var memberError))
        {
            throw memberError;
        }

        var loader = ModrinthHelper.GetVersionLoaderFilter(project.ProjectTypes.FirstOrDefault(), filter.Loader);
        var versions = await client
                             .GetProjectVersionsAsync(id.Identity,
                                                      null,
                                                      loader is not null ? ArrayParameterConstructor([loader]) : null,
                                                      BuildLoaderFields(("game_versions", filter.Version)),
                                                      limit: 1)
                             .ConfigureAwait(false);
        var chosen = versions.OrderByDescending(x => x.DatePublished).FirstOrDefault()
                  ?? throw new ResourceNotFoundException(
                         $"{project.Name} ({label}:{id.Identity}@*) has no matched version for {FormatTarget(filter)}");
        return ModrinthHelper.ToPackage(label, project, chosen, members.Successful[project.Id]);
    }

    private async Task<RepositoryHelper.BatchResult<ScopedPackageIdentifier, Package>> ResolveKnownVersionsAsync(
        ScopedPackageIdentifier[] knownVids,
        IReadOnlyDictionary<string, ProjectInfo> projects,
        RepositoryHelper.BatchResult<string, MemberInfo> members)
    {
        var result = new RepositoryHelper.BatchResult<ScopedPackageIdentifier, Package>();
        var versionIds = knownVids.Select(x => x.Version!).Distinct().ToArray();

        List<VersionInfo> versions;
        try
        {
            versions = (await client.GetMultipleVersionsAsync(ArrayParameterConstructor(versionIds))
                                    .ConfigureAwait(false))
                       .ToList();
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            return result.FailAll(knownVids, ex);
        }

        var versionById = versions.ToDictionary(x => x.Id);
        foreach (var id in knownVids)
        {
            if (!versionById.TryGetValue(id.Version!, out var version))
            {
                result.Fail(id,
                    new ResourceNotFoundException($"{id.Identity}/{id.Version} not found in the repository"));
                continue;
            }

            try
            {
                var project = projects.GetValueOrDefault(version.ProjectId)
                            ?? throw new ResourceNotFoundException(
                                   $"{version.ProjectId} not found in the repository");
                if (members.Failed.TryGetValue(project.Id, out var memberError))
                {
                    throw memberError;
                }

                result.Succeed(id,
                    ModrinthHelper.ToPackage(label, project, version, members.Successful[project.Id]));
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                result.Fail(id, ex);
            }
        }

        return result;
    }

    public async Task<string> ReadDescriptionAsync(ScopedProjectIdentifier id)
    {
        var project = await client.GetProjectAsync(id.Identity).ConfigureAwait(false);
        return project.Description;
    }

    public async Task<string> ReadChangelogAsync(ScopedPackageIdentifier id)
    {
        if (id.Version is null)
        {
            return string.Empty;
        }

        var version = await client.GetVersionAsync(id.Version).ConfigureAwait(false);
        return version.Changelog ?? string.Empty;
    }

    public async Task<IPaginationHandle<Version>> InspectAsync(ScopedProjectIdentifier id, Filter filter)
    {
        var project = await client.GetProjectAsync(id.Identity).ConfigureAwait(false);
        var loader = ModrinthHelper.GetVersionLoaderFilter(project.ProjectTypes.FirstOrDefault(), filter.Loader);
        var first = await client
                         .GetProjectVersionsAsync(id.Identity,
                                                  null,
                                                  loader is not null ? ArrayParameterConstructor([loader]) : null,
                                                  BuildLoaderFields(("game_versions", filter.Version)))
                         .ConfigureAwait(false);
        var all = first.Select(x => ModrinthHelper.ToVersion(label, x)).ToList();
        // Modrinth 的版本无法分页，只能过滤拉取全部之后本地分页
        return new LocalPaginationHandle<Version>(all, PAGE_SIZE);
    }

    #endregion
}
