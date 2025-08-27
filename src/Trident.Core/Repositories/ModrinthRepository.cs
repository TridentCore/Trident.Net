using System.Diagnostics;
using System.Net;
using Trident.Core.Clients;
using Trident.Core.Utilities;
using Refit;
using Trident.Abstractions.Repositories;
using Trident.Abstractions.Repositories.Resources;
using Trident.Core.Models.ModrinthApi;
using Version = Trident.Abstractions.Repositories.Resources.Version;

namespace Trident.Core.Repositories
{
    public class ModrinthRepository(IModrinthClient client) : IRepository
    {
        private const uint PAGE_SIZE = 20;

        public string Label => ModrinthHelper.LABEL;

        private string ArrayParameterConstructor(IEnumerable<string> members) {
            return "[\"" + string.Join("\",\"",members) + "\"]";
        }

        #region IRepository Members

        public async Task<RepositoryStatus> CheckStatusAsync()
        {
            var (loadersTask, versionsTask, typesTask) = (client.GetLoadersAsync(), client.GetGameVersionsAsync(),
                                                          client.GetProjectTypesAsync());
            var (loaders, versions, types) = (ModrinthHelper.ToLoaderNames(await loadersTask.ConfigureAwait(false)),
                                              ModrinthHelper.ToVersionNames(await versionsTask.ConfigureAwait(false)),
                                              await typesTask.ConfigureAwait(false));
            var supportedLoaders = loaders
                                  .Select(x => ModrinthHelper.MODLOADER_MAPPINGS.GetValueOrDefault(x))
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
            var initial = first.Hits.Select(ModrinthHelper.ToExhibit);
            return new PaginationHandle<Exhibit>(initial,
                                                 first.Limit,
                                                 first.TotalHits,
                                                 async (pageIndex, _) =>
                                                 {
                                                     var rv = await client
                                                                   .SearchAsync(query,
                                                                                    ModrinthHelper
                                                                                       .BuildFacets(ModrinthHelper
                                                                                               .ResourceKindToType(filter
                                                                                                   .Kind),
                                                                                            filter.Version,
                                                                                            loader),
                                                                                    offset: pageIndex * first.Limit,
                                                                                    limit: first.Limit)
                                                                   .ConfigureAwait(false);
                                                     var exhibits = rv.Hits.Select(ModrinthHelper.ToExhibit).ToList();
                                                     return exhibits;
                                                 });
        }

        public async Task<Project> QueryAsync(string? ns, string pid)
        {
            var project = await client.GetProjectAsync(pid).ConfigureAwait(false);
            var team = await client.GetTeamMembersAsync(project.TeamId).ConfigureAwait(false);
            return ModrinthHelper.ToProject(project, team.FirstOrDefault());
        }

        public Task<IEnumerable<Project>> QueryBatchAsync(IEnumerable<(string?, string pid)> batch) =>
            throw new NotImplementedException();

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
                    return ModrinthHelper.ToPackage(project, version, members.FirstOrDefault());
                }
                else
                {
                    var (versionTask, membersTask) =
                        (client
                        .GetProjectVersionsAsync(pid,
                                                 null,
                                                 filter.Loader is not null
                                                     ? $"[\"{ModrinthHelper.LoaderIdToName(filter.Loader)}\"]"
                                                     : null)
                        .ConfigureAwait(false), client.GetTeamMembersAsync(project.TeamId).ConfigureAwait(false));
                    var (version, members) = (await versionTask, await membersTask);
                    var found = version.FirstOrDefault(x => filter.Version is null
                                                         || x.GameVersions.Contains(filter.Version));
                    if (found == default)
                    {
                        throw new ResourceNotFoundException($"{pid}/{vid ?? "*"} has not matched version");
                    }

                    return ModrinthHelper.ToPackage(project, found, members.FirstOrDefault());
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

        public async Task<List<Package>> BulkResolveAsync(List<(string? ns,string pid,string? vid)> bml, Filter filter, bool detailed = false) {
            var projs = await client.BulkGetProjectsAsync(ArrayParameterConstructor(bml.Select((bm => bm.pid)))).ConfigureAwait(false);
            Dictionary<string,VersionInfo> vdt = [];
            List<string> vql = [];
            foreach (var bm in projs.Select(pi => bml.FirstOrDefault(bm => bm.pid == pi.Id))) {
                if (bm.vid == null) {
                    var version = await client
                                       .GetProjectVersionsAsync(bm.pid,null,filter.Loader is not null ? $"[\"{ModrinthHelper.LoaderIdToName(filter.Loader)}\"]" : null)
                                       .ConfigureAwait(false);
                    var found = version.FirstOrDefault(x => filter.Version is null
                                                         || x.GameVersions.Contains(filter.Version));
                    if (found == default) {
                        throw new ResourceNotFoundException($"{bm.pid}/{bm.vid ?? "*"} has not matched version");
                    }
                    vdt.Add(bm.pid,found);
                } else {
                    vql.Add(bm.vid);
                }
            }
            if (vql.Count != 0) {
                var vrl = await client.BulkGetVersionsAsync(
                                                            ArrayParameterConstructor(vql)
                                                           )!.ConfigureAwait(false);
                foreach (var vr in vrl) {
                    vdt.Add(vr.ProjectId,vr);
                }
            }
            List<Package> result = [];
            foreach (var pi in projs) {

                var member = detailed
                                 ? (await client.GetTeamMembersAsync(pi.TeamId).ConfigureAwait(false)).FirstOrDefault()
                                 : new(); // 避免不可避免的大量请求
                if (vdt.Keys.Contains(pi.Id)) {
                    result.Add(ModrinthHelper.ToPackage(pi,vdt[pi.Id],member));
                } else {
                    var bm = bml.FirstOrDefault(bm => bm.pid == pi.Id);
                    Debug.WriteLine($"Couldn't found fetched version in vDict {pi.Id}:{bm.vid ?? "*"}","Warn");
                    try {
                        if (bm.vid != null) {
                            result.Add(ModrinthHelper.ToPackage(pi,await client.GetVersionAsync(bm.vid).ConfigureAwait(false),member));
                        } else {
                            // 这怎么可能(恼
                            Debug.WriteLine($"{pi.Id}/{bm.vid} not found in the repository","Error");
                        }
                    }
                    catch(ApiException ex) {
                        if (ex.StatusCode == HttpStatusCode.NotFound) {
                            throw new ResourceNotFoundException($"{pi.Id}/{bm.vid ?? "*"} not found in the repository");
                        }
                        throw;
                    }
                }
            }
            return result;
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
                     .Select(ModrinthHelper.ToVersion)
                     .ToList();
            // Modrinth 的版本无法分页，只能过滤拉取全部之后本地分页
            return new LocalPaginationHandle<Version>(all, PAGE_SIZE);
        }

        #endregion
    }
}
