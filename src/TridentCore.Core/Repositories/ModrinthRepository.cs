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

    #region IRepository Members

    public async Task<RepositoryStatus> CheckStatusAsync()
    {
        var (loadersTask, versionsTask, typesTask) = (
            client.GetLoadersAsync(),
            client.GetGameVersionsAsync(),
            client.GetProjectTypesAsync()
        );
        var (loaders, versions, types) = (
            ModrinthHelper.ToLoaderNames(await loadersTask.ConfigureAwait(false)),
            ModrinthHelper.ToVersionNames(await versionsTask.ConfigureAwait(false)),
            await typesTask.ConfigureAwait(false)
        );
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
            .SearchAsync(
                query,
                ModrinthHelper.BuildFacets(
                    ModrinthHelper.ResourceKindToType(filter.Kind),
                    filter.Version,
                    loader
                ),
                limit: PAGE_SIZE
            )
            .ConfigureAwait(false);
        var initial = first.Hits.Select(x => ModrinthHelper.ToExhibit(label, x));
        return new PaginationHandle<Exhibit>(
            initial,
            first.Limit,
            first.TotalHits,
            async (pageIndex, _) =>
            {
                var rv = await client
                    .SearchAsync(
                        query,
                        ModrinthHelper.BuildFacets(
                            ModrinthHelper.ResourceKindToType(filter.Kind),
                            filter.Version,
                            loader
                        ),
                        offset: pageIndex * first.Limit,
                        limit: first.Limit
                    )
                    .ConfigureAwait(false);
                var exhibits = rv.Hits.Select(x => ModrinthHelper.ToExhibit(label, x)).ToList();
                return exhibits;
            }
        );
    }

    public async Task<Package> IdentifyAsync(ReadOnlyMemory<byte> content)
    {
        var hash = Convert.ToHexString(SHA1.HashData(content.Span));
        var info = await client.GetVersionFromHashAsync(hash).ConfigureAwait(false);
        var project = await client.GetProjectAsync(info.ProjectId).ConfigureAwait(false);
        var members = await client.GetTeamMembersAsync(project.TeamId).ConfigureAwait(false);
        return ModrinthHelper.ToPackage(label, project, info, members.FirstOrDefault());
    }

    public async Task<Project> QueryAsync(ScopedProjectIdentifier id)
    {
        var project = await client.GetProjectAsync(id.Identity).ConfigureAwait(false);
        var members = await client.GetTeamMembersAsync(project.TeamId).ConfigureAwait(false);
        return ModrinthHelper.ToProject(label, project, members.FirstOrDefault());
    }

    public async Task<BatchResolveResult<ScopedProjectIdentifier, Project>> QueryBatchAsync(
        IEnumerable<ScopedProjectIdentifier> batch
    )
    {
        var batchArray = batch.ToArray();
        var successful = new Dictionary<ScopedProjectIdentifier, Project>();
        var failed = new Dictionary<ScopedProjectIdentifier, Exception>();

        Dictionary<string, ProjectInfo> projects;
        try
        {
            projects = (
                await client
                    .GetMultipleProjectsAsync(
                        ArrayParameterConstructor(batchArray.Select(bm => bm.Identity))
                    )
                    .ConfigureAwait(false)
            ).ToDictionary(x => x.Id);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            foreach (var x in batchArray)
                failed[x] = ex;
            return new(successful, failed);
        }

        var teamResults = await Task.WhenAll(projects.Values.Select(async project =>
        {
            try
            {
                var member = (await client.GetTeamMembersAsync(project.TeamId).ConfigureAwait(false))
                    .FirstOrDefault()
                    ?? throw new ResourceNotFoundException(
                        $"{project.Name} ({label}:{project.Id}) has no team member in the repository"
                    );
                return (project.Id, Member: member, Error: (Exception?)null);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                return (project.Id, Member: (MemberInfo?)null, Error: ex);
            }
        })).ConfigureAwait(false);
        var teams = teamResults
            .Where(x => x.Error is null)
            .ToDictionary(x => x.Id, x => x.Member!);
        var teamErrors = teamResults
            .Where(x => x.Error is not null)
            .ToDictionary(x => x.Id, x => x.Error!);

        foreach (var id in batchArray)
        {
            if (!projects.TryGetValue(id.Identity, out var project))
            {
                failed[id] = new ResourceNotFoundException(
                    $"{id.Identity} not found in the repository"
                );
                continue;
            }

            if (teamErrors.TryGetValue(project.Id, out var teamError))
            {
                failed[id] = teamError;
                continue;
            }

            successful[id] = ModrinthHelper.ToProject(label, project, teams[project.Id]);
        }

        return new(successful, failed);
    }

    public async Task<Package> ResolveAsync(ScopedPackageIdentifier id, Filter filter)
    {
        try
        {
            var project = await client.GetProjectAsync(id.Identity).ConfigureAwait(false);
            if (id.Version != null)
            {
                var (versionTask, membersTask) = (
                    client.GetVersionAsync(id.Version).ConfigureAwait(false),
                    client.GetTeamMembersAsync(project.TeamId).ConfigureAwait(false)
                );
                var (version, members) = (await versionTask, await membersTask);
                return ModrinthHelper.ToPackage(label, project, version, members.FirstOrDefault());
            }
            else
            {
                var loader = ModrinthHelper.GetVersionLoaderFilter(
                    project.ProjectTypes.FirstOrDefault(),
                    filter.Loader
                );
                var (versionsTask, membersTask) = (
                    client
                        .GetProjectVersionsAsync(
                            id.Identity,
                            null,
                            loader is not null ? ArrayParameterConstructor([loader]) : null,
                            BuildLoaderFields(("game_versions", filter.Version)),
                            limit: 1
                        )
                        .ConfigureAwait(false),
                    client.GetTeamMembersAsync(project.TeamId).ConfigureAwait(false)
                );
                var (versions, members) = (await versionsTask, await membersTask);
                var found = versions
                    .OrderByDescending(x => x.DatePublished)
                    .FirstOrDefault();
                if (found == null)
                {
                    throw new ResourceNotFoundException(
                        $"{project.Name} ({label}:{id.Identity}@*) has no matched version for {FormatTarget(filter)}"
                    );
                }

                return ModrinthHelper.ToPackage(label, project, found, members.FirstOrDefault());
            }
        }
        catch (ApiException ex)
        {
            if (ex.StatusCode == HttpStatusCode.NotFound)
            {
                throw new ResourceNotFoundException(
                    $"{id.Identity}/{id.Version ?? "*"} not found in the repository"
                );
            }

            throw;
        }
    }

    public async Task<BatchResolveResult<ScopedPackageIdentifier, Package>> ResolveBatchAsync(
        IEnumerable<ScopedPackageIdentifier> batch,
        Filter filter
    )
    {
        var batchArray = batch.ToArray();
        var knownVids = batchArray.Where(x => x.Version is not null).ToArray();
        var unknownVids = batchArray.Where(x => x.Version is null).ToArray();

        var successful = new Dictionary<ScopedPackageIdentifier, Package>();
        var failed = new Dictionary<ScopedPackageIdentifier, Exception>();

        Dictionary<string, ProjectInfo> projects;
        try
        {
            projects = (
                await client
                    .GetMultipleProjectsAsync(
                        ArrayParameterConstructor(batchArray.Select(bm => bm.Identity))
                    )
                    .ConfigureAwait(false)
            ).ToDictionary(x => x.Id);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            foreach (var x in batchArray)
                failed[x] = ex;
            return new(successful, failed);
        }

        var memberResults = await Task.WhenAll(projects.Values.Select(async project =>
        {
            try
            {
                var member = (await client.GetTeamMembersAsync(project.TeamId).ConfigureAwait(false))
                    .FirstOrDefault()
                    ?? throw new ResourceNotFoundException(
                        $"{project.Name} ({label}:{project.Id}) has no team member in the repository"
                    );
                return (ProjectId: project.Id, Member: member, Error: (Exception?)null);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                return (ProjectId: project.Id, Member: (MemberInfo?)null, Error: ex);
            }
        })).ConfigureAwait(false);
        var members = memberResults
            .Where(x => x.Error is null)
            .ToDictionary(x => x.ProjectId, x => x.Member!);
        var memberErrors = memberResults
            .Where(x => x.Error is not null)
            .ToDictionary(x => x.ProjectId, x => x.Error!);

        // 这一块依旧没法一次性拿全，都怪 Modrinth 的 API 设计。
        // 每条独立请求，逐条归因，单条失败不拖累其它。
        var unknownResults = await Task.WhenAll(unknownVids.Select(async x =>
        {
            try
            {
                var project = projects.GetValueOrDefault(x.Identity)
                              ?? throw new ResourceNotFoundException(
                                  $"{x.Identity} not found in the repository"
                              );
                if (memberErrors.TryGetValue(project.Id, out var memberError))
                    throw memberError;

                var loader = ModrinthHelper.GetVersionLoaderFilter(
                    project.ProjectTypes.FirstOrDefault(),
                    filter.Loader
                );
                var versions = await client
                    .GetProjectVersionsAsync(
                        x.Identity,
                        null,
                        loader is not null ? ArrayParameterConstructor([loader]) : null,
                        BuildLoaderFields(("game_versions", filter.Version)),
                        limit: 1
                    )
                    .ConfigureAwait(false);
                var chosen = versions
                    .OrderByDescending(y => y.DatePublished)
                    .FirstOrDefault();
                if (chosen == null)
                {
                    throw new ResourceNotFoundException(
                        $"{project.Name} ({label}:{x.Identity}@*) has no matched version for {FormatTarget(filter)}"
                    );
                }

                return (
                    Id: x,
                    Package: ModrinthHelper.ToPackage(
                        label,
                        project,
                        chosen,
                        members[project.Id]
                    ),
                    Error: (Exception?)null
                );
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                return (Id: x, Package: (Package?)null, Error: ex);
            }
        })).ConfigureAwait(false);

        foreach (var r in unknownResults)
        {
            if (r.Error is not null)
                failed[r.Id] = r.Error;
            else
                successful[r.Id] = r.Package!;
        }

        // knownVids 走批量版本接口；失败时该批整体进 failed。
        if (knownVids.Length > 0)
        {
            var knownGroups = knownVids.ToLookup(x => x.Version!);
            try
            {
                var knownVersions = await client
                    .GetMultipleVersionsAsync(
                        ArrayParameterConstructor(knownGroups.Select(x => x.Key))
                    )
                    .ConfigureAwait(false);

                foreach (var v in knownVersions)
                {
                    foreach (var id in knownGroups[v.Id])
                    {
                        try
                        {
                            var project = projects.GetValueOrDefault(v.ProjectId)
                                          ?? throw new ResourceNotFoundException(
                                              $"{v.ProjectId} not found in the repository"
                                          );
                            if (memberErrors.TryGetValue(project.Id, out var memberError))
                                throw memberError;

                            successful[id] = ModrinthHelper.ToPackage(
                                label,
                                project,
                                v,
                                members[project.Id]
                            );
                        }
                        catch (OperationCanceledException)
                        {
                            throw;
                        }
                        catch (Exception ex)
                        {
                            failed[id] = ex;
                        }
                    }
                }

                foreach (var id in knownVids)
                {
                    if (!successful.ContainsKey(id) && !failed.ContainsKey(id))
                        failed[id] = new ResourceNotFoundException(
                            $"{id.Identity}/{id.Version} not found in the repository"
                        );
                }
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                foreach (var x in knownVids)
                {
                    if (!successful.ContainsKey(x))
                        failed[x] = ex;
                }
            }
        }

        return new(successful, failed);
    }

    public async Task<string> ReadDescriptionAsync(ScopedProjectIdentifier id)
    {
        var project = await client.GetProjectAsync(id.Identity).ConfigureAwait(false);
        return project.Description;
    }

    public async Task<string> ReadChangelogAsync(ScopedPackageIdentifier id)
    {
        var version = await client.GetVersionAsync(id.Version).ConfigureAwait(false);
        return version.Changelog ?? string.Empty;
    }

    public async Task<IPaginationHandle<Version>> InspectAsync(
        ScopedProjectIdentifier id,
        Filter filter
    )
    {
        var project = await client.GetProjectAsync(id.Identity).ConfigureAwait(false);
        var loader = ModrinthHelper.GetVersionLoaderFilter(
            project.ProjectTypes.FirstOrDefault(),
            filter.Loader
        );
        var first = await client
            .GetProjectVersionsAsync(
                id.Identity,
                null,
                loader is not null ? ArrayParameterConstructor([loader]) : null,
                BuildLoaderFields(("game_versions", filter.Version))
            )
            .ConfigureAwait(false);
        var all = first
            .Select(x => ModrinthHelper.ToVersion(label, x))
            .ToList();
        // Modrinth 的版本无法分页，只能过滤拉取全部之后本地分页
        return new LocalPaginationHandle<Version>(all, PAGE_SIZE);
    }

    #endregion

    private static string ArrayParameterConstructor(IEnumerable<string?> members) =>
        JsonSerializer.Serialize(members.Where(x => x is not null).ToArray());

    // v3 把 game_versions 等过滤并进了 loader_fields（JSON 对象 {"game_versions":[...]}），
    // 没有独立的 game_versions 参数（v2 才有，容易踩坑）
    private static string? BuildLoaderFields(params (string Key, string? Value)[] fields)
    {
        var dict = fields
            .Where(f => f.Value is not null)
            .ToDictionary(f => f.Key, f => new[] { f.Value });
        return dict.Count == 0 ? null : JsonSerializer.Serialize(dict);
    }

    private static string FormatTarget(Filter filter) => $"{filter.Version ?? "*"}/{filter.Loader ?? "*"}";
}
