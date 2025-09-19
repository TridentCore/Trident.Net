using System.Diagnostics;
using System.Net;
using System.Security.Cryptography;
using Trident.Core.Clients;
using Trident.Core.Utilities;
using Refit;
using Trident.Abstractions.Repositories;
using Trident.Abstractions.Repositories.Resources;
using Trident.Core.Models.ModrinthApi;
using Version = Trident.Abstractions.Repositories.Resources.Version;

namespace Trident.Core.Repositories;

public class ModrinthRepository(string label, IModrinthClient client) : IRepository
{
    private const uint PAGE_SIZE = 20;

    private static string ArrayParameterConstructor(IEnumerable<string?> members) =>
        "[\"" + string.Join("\",\"", members.Where(x => x is not null)) + "\"]";

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
        var loader = filter.Kind is ResourceKind.Mod ? ModrinthHelper.LoaderIdToName(filter.Loader) : null;
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

    public async Task<Project> QueryAsync(string? ns, string pid)
    {
        var project = await client.GetProjectAsync(pid).ConfigureAwait(false);
        var members = await client.GetTeamMembersAsync(project.TeamId).ConfigureAwait(false);
        return ModrinthHelper.ToProject(label, project, members.FirstOrDefault());
    }

    public async Task<IReadOnlyList<Project>> QueryBatchAsync(IEnumerable<(string?, string pid)> batch)
    {
        var batchArray = batch.ToArray();
        var projects = await client
                            .GetMultipleProjectsAsync(ArrayParameterConstructor(batchArray.Select(bm => bm.pid)))
                            .ConfigureAwait(false);
        var teamTasks = projects
                       .Select(async x => (x.Id, await client.GetTeamMembersAsync(x.TeamId).ConfigureAwait(false)))
                       .ToList();
        await Task.WhenAll(teamTasks).ConfigureAwait(false);
        var teams = teamTasks.ToDictionary(x => x.Result.Id, x => x.Result.Item2.FirstOrDefault());
        return projects.Select(x => ModrinthHelper.ToProject(label, x, teams[x.Id])).ToList();
    }

    public async Task<Package> ResolveAsync(string? ns, string pid, string? vid, Filter filter)
    {
        try
        {
            var project = await client.GetProjectAsync(pid).ConfigureAwait(false);
            if (vid != null)
            {
                var (versionTask, membersTask) = (client.GetVersionAsync(vid).ConfigureAwait(false),
                                                  client.GetTeamMembersAsync(project.TeamId).ConfigureAwait(false));
                var (version, members) = (await versionTask, await membersTask);
                return ModrinthHelper.ToPackage(label, project, version, members.FirstOrDefault());
            }
            else
            {
                var (versionsTask, membersTask) =
                    (client
                    .GetProjectVersionsAsync(pid,
                                             null,
                                             filter.Loader is not null
                                                 ? ArrayParameterConstructor([
                                                     ModrinthHelper.LoaderIdToName(filter.Loader)
                                                 ])
                                                 : null)
                    .ConfigureAwait(false), client.GetTeamMembersAsync(project.TeamId).ConfigureAwait(false));
                var (versions, members) = (await versionsTask, await membersTask);
                var found = versions
                           .OrderByDescending(x => x.DatePublished)
                           .FirstOrDefault(x => filter.Version is null || x.GameVersions.Contains(filter.Version));
                if (found == null)
                {
                    throw new ResourceNotFoundException($"{pid}/{vid ?? "*"} has not matched version");
                }

                return ModrinthHelper.ToPackage(label, project, found, members.FirstOrDefault());
            }
        }
        catch (ApiException ex)
        {
            if (ex.StatusCode == HttpStatusCode.NotFound)
            {
                throw new ResourceNotFoundException($"{pid}/{vid ?? "*"} not found in the repository");
            }

            throw;
        }
    }

    public async Task<IReadOnlyList<Package>> ResolveBatchAsync(
        IEnumerable<(string? ns, string pid, string? vid)> batch,
        Filter filter)
    {
        var batchArray = batch.ToArray();
        var knownVids = batchArray.Where(x => x.vid is not null);
        var unknownVids = batchArray.Where(x => x.vid is null);

        // 这一块依旧没法一次性拿全，都怪 Modrinth 的 API 设计
        var unknownProjectVersionsTasks = unknownVids
                                         .Select(async x =>
                                          {
                                              var versions = await client
                                                                  .GetProjectVersionsAsync(x.pid,
                                                                       null,
                                                                       filter.Loader is not null
                                                                           ? ArrayParameterConstructor([
                                                                               ModrinthHelper.LoaderIdToName(filter
                                                                                  .Loader)
                                                                           ])
                                                                           : null)
                                                                  .ConfigureAwait(false);
                                              var chosen = versions
                                                          .OrderByDescending(y => y.DatePublished)
                                                          .FirstOrDefault(y => filter.Version is null
                                                                            || y.GameVersions.Contains(filter.Version));
                                              if (chosen == null)
                                                  throw new
                                                      ResourceNotFoundException($"{x.pid}/{x.vid ?? "*"} has not matched version");
                                              return chosen;
                                          })
                                         .ToList();
        await Task.WhenAll(unknownProjectVersionsTasks).ConfigureAwait(false);
        var unknownVersions = unknownProjectVersionsTasks.Select(x => x.Result);

        var knownVersions = await client
                                 .GetMultipleVersionsAsync(ArrayParameterConstructor(knownVids.Select(bm => bm.vid)))
                                 .ConfigureAwait(false);


        var projects = (await client
                             .GetMultipleProjectsAsync(ArrayParameterConstructor(batchArray.Select(bm => bm.pid)))
                             .ConfigureAwait(false)).ToDictionary(x => x.Id);
        var membersTasks = projects
                          .Keys.Select(async x =>
                                           (Id: x, Members: await client.GetTeamMembersAsync(x).ConfigureAwait(false)))
                          .ToList();
        await Task.WhenAll(membersTasks).ConfigureAwait(false);
        var members = membersTasks.ToDictionary(x => x.Result.Id, x => x.Result.Members.FirstOrDefault());

        var packages = knownVersions
                      .Concat(unknownVersions)
                      .Select(x => ModrinthHelper.ToPackage(label, projects[x.ProjectId], x, members[x.ProjectId]))
                      .ToList();
        return packages;
    }

    public async Task<string> ReadDescriptionAsync(string? ns, string pid)
    {
        var project = await client.GetProjectAsync(pid).ConfigureAwait(false);
        return project.Description;
    }

    public async Task<string> ReadChangelogAsync(string? ns, string pid, string vid)
    {
        var version = await client.GetVersionAsync(vid).ConfigureAwait(false);
        return version.Changelog ?? string.Empty;
    }

    public async Task<IPaginationHandle<Version>> InspectAsync(string? ns, string pid, Filter filter)
    {
        var project = await client.GetProjectAsync(pid).ConfigureAwait(false);
        var type = project.ProjectTypes.FirstOrDefault();
        var loader = type == ModrinthHelper.RESOURCENAME_MOD ? ModrinthHelper.LoaderIdToName(filter.Loader) : null;
        var first = await client
                         .GetProjectVersionsAsync(pid, null, loader is not null ? $"[\"{loader}\"]" : null)
                         .ConfigureAwait(false);
        var all = first
                 .Where(x => filter.Version is null || x.GameVersions.Contains(filter.Version))
                 .Select(x => ModrinthHelper.ToVersion(label, x))
                 .ToList();
        // Modrinth 的版本无法分页，只能过滤拉取全部之后本地分页
        return new LocalPaginationHandle<Version>(all, PAGE_SIZE);
    }

    #endregion
}
