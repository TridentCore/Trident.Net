using System.Net;
using Refit;
using ReverseMarkdown;
using TridentCore.Abstractions.Repositories;
using TridentCore.Abstractions.Repositories.Resources;
using TridentCore.Abstractions.Utilities;
using TridentCore.Core.Clients;
using TridentCore.Core.Utilities;
using TridentCore.Pref;
using Version = TridentCore.Abstractions.Repositories.Resources.Version;
using FileInfo = TridentCore.Core.Models.CurseForgeApi.FileInfo;

namespace TridentCore.Core.Repositories;

public class CurseForgeRepository(string label, ICurseForgeClient client) : IRepository
{
    private const uint PAGE_SIZE = 20;

    private static readonly Converter CONVERTER = new(new()
    {
        Flavor = Config.MarkdownFlavor.Default,
        Links = { SmartHref = true }
    });

    private static string FormatTarget(Filter filter) => $"{filter.Version ?? "*"}/{filter.Loader ?? "*"}";

    #region IRepository Members

    public async Task<RepositoryStatus> CheckStatusAsync()
    {
        var raw = await client.GetMinecraftVersionsAsync().ConfigureAwait(false);
        var versions = raw.Data.Select(x => x.VersionString).ToList();
        return new([
                       LoaderHelper.LOADERID_NEOFORGE,
                       LoaderHelper.LOADERID_FORGE,
                       LoaderHelper.LOADERID_FABRIC,
                       LoaderHelper.LOADERID_QUILT
                   ],
                   versions,
                   [
                       ResourceKind.Modpack,
                       ResourceKind.Mod,
                       ResourceKind.ResourcePack,
                       ResourceKind.ShaderPack,
                       ResourceKind.World,
                       ResourceKind.DataPack
                   ]);
    }

    public async Task<IPaginationHandle<Exhibit>> SearchAsync(string query, Filter filter)
    {
        var loader = filter.Kind is ResourceKind.Mod or ResourceKind.Modpack
                         ? CurseForgeHelper.LoaderIdToType(filter.Loader)
                         : null;

        var first = await client
                         .SearchModsAsync(query,
                                          CurseForgeHelper.ResourceKindToClassId(filter.Kind),
                                          filter.Version,
                                          loader,
                                          pageSize: PAGE_SIZE)
                         .ConfigureAwait(false);
        var initial = first.Data.Select(x => CurseForgeHelper.ToExhibit(label, x));
        return new PaginationHandle<Exhibit>(initial,
                                             first.Pagination.PageSize,
                                             first.Pagination.TotalCount,
                                             async (pageIndex, _) =>
                                             {
                                                 var rv = await client
                                                               .SearchModsAsync(query,
                                                                                    CurseForgeHelper
                                                                                       .ResourceKindToClassId(filter
                                                                                           .Kind),
                                                                                    filter.Version,
                                                                                    loader,
                                                                                    index: pageIndex
                                                                                      * first.Pagination.PageSize,
                                                                                    pageSize: first.Pagination.PageSize)
                                                               .ConfigureAwait(false);
                                                 var exhibits = rv
                                                               .Data.Select(x => CurseForgeHelper.ToExhibit(label, x))
                                                               .ToList();
                                                 return exhibits;
                                             });
    }

    public async Task<Package> IdentifyAsync(ReadOnlyMemory<byte> content)
    {
        var hash = CurseForgeHelper.ComputeFingerprint(content);
        var rv = await client.GetFingerprintMatchesByGameId(new([(uint)hash])).ConfigureAwait(false);
        var match = rv.Data.ExactMatches.FirstOrDefault();
        if (match != null)
        {
            var mod = (await client.GetModAsync(match.Id).ConfigureAwait(false)).Data;
            return CurseForgeHelper.ToPackage(label, mod, match.File);
        }

        throw new ResourceNotFoundException($"No file matched the fingerprint {hash}");
    }

    public async Task<IReadOnlyList<Package?>> IdentifyBatchAsync(
        IEnumerable<ReadOnlyMemory<byte>> contents,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var list = contents.ToList();
        var results = new Package?[list.Count];

        var items = Enumerable.Range(0, list.Count)
                              .Select(i => (index: i, fingerprint: (uint)CurseForgeHelper.ComputeFingerprint(list[i])))
                              .ToArray();
        if (items.Length == 0)
            return results;

        var resp = await client
            .GetFingerprintMatchesByGameId(new([.. items.Select(x => x.fingerprint)]))
            .ConfigureAwait(false);
        var byFingerprint = resp.Data.ExactMatches
                                .ToDictionary(match => (uint)match.File.FileFingerprint,
                                              match => (match.Id, match.File));

        if (byFingerprint.Count == 0)
            return results;

        var mods = (await client
                         .GetModsAsync(new([.. byFingerprint.Values.Select(x => x.Id).Distinct()]))
                         .ConfigureAwait(false))
                   .Data
                   .ToDictionary(m => m.Id);

        foreach (var (index, fingerprint) in items)
        {
            if (!byFingerprint.TryGetValue(fingerprint, out var match))
                continue;
            if (!mods.TryGetValue(match.Id, out var mod))
                continue;
            results[index] = CurseForgeHelper.ToPackage(label, mod, match.File);
        }

        return results;
    }

    public async Task<PackageIdentifier> RecognizeAsync(Uri uri, CancellationToken cancellationToken = default)
    {
        if (uri.Host.EndsWith("forgecdn.net", StringComparison.OrdinalIgnoreCase))
        {
            if (!TryExtractFileId(uri, out var fileId))
            {
                throw new ResourceNotFoundException($"{uri} is not a forgecdn file URL");
            }

            var file = (await client.GetFilesAsync(new([fileId])).ConfigureAwait(false)).Data.FirstOrDefault();
            if (file is null)
            {
                throw new ResourceNotFoundException($"CurseForge file {fileId} not found");
            }

            return new(label, null, file.ModId.ToString(), fileId.ToString());
        }

        if (!uri.Host.EndsWith("curseforge.com", StringComparison.OrdinalIgnoreCase))
        {
            throw new ResourceNotFoundException($"{uri} is not a curseforge URL");
        }

        var (slug, fileIdStr) = ExtractReference(uri);
        if (string.IsNullOrEmpty(slug))
        {
            throw new ResourceNotFoundException($"{uri} has no project slug");
        }

        cancellationToken.ThrowIfCancellationRequested();
        var resp = await client.SearchModsAsync(null, null, null, null, slug: slug).ConfigureAwait(false);
        var mod = resp.Data.FirstOrDefault();
        if (mod is null)
        {
            throw new ResourceNotFoundException($"{slug} not found in the repository");
        }

        return new(label, null, mod.Id.ToString(), fileIdStr);
    }

    public async Task<BatchResolveResult<Uri, PackageIdentifier>> RecognizeBatchAsync(
        IEnumerable<Uri> uris, CancellationToken cancellationToken = default)
    {
        var result = new RepositoryHelper.BatchResult<Uri, PackageIdentifier>();
        var byFileId = new Dictionary<uint, List<Uri>>();
        var slugUris = new List<(Uri Uri, string Slug)>();

        foreach (var uri in uris)
        {
            if (uri.Host.EndsWith("forgecdn.net", StringComparison.OrdinalIgnoreCase))
            {
                if (TryExtractFileId(uri, out var fileId))
                {
                    if (!byFileId.TryGetValue(fileId, out var list))
                    {
                        byFileId[fileId] = list = [];
                    }

                    list.Add(uri);
                }
                else
                {
                    result.Fail(uri, new ResourceNotFoundException($"{uri} is not a forgecdn file URL"));
                }
            }
            else if (uri.Host.EndsWith("curseforge.com", StringComparison.OrdinalIgnoreCase))
            {
                var (slug, _) = ExtractReference(uri);
                if (string.IsNullOrEmpty(slug))
                {
                    result.Fail(uri, new ResourceNotFoundException($"{uri} has no project slug"));
                }
                else
                {
                    slugUris.Add((uri, slug));
                }
            }
            else
            {
                result.Fail(uri, new ResourceNotFoundException($"{uri} is not a curseforge URL"));
            }
        }

        // One GetFilesAsync covers every forgecdn uri in the batch, deduped by file id.
        if (byFileId.Count > 0)
        {
            Dictionary<uint, FileInfo> fileById;
            try
            {
                var files = (await client.GetFilesAsync(new([.. byFileId.Keys])).ConfigureAwait(false)).Data;
                fileById = files.ToDictionary(x => x.Id);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                foreach (var list in byFileId.Values)
                {
                    foreach (var uri in list)
                    {
                        result.Fail(uri, ex);
                    }
                }

                fileById = [];
            }

            foreach (var (fileId, list) in byFileId)
            {
                if (fileById.TryGetValue(fileId, out var info))
                {
                    var id = new PackageIdentifier(label, null, info.ModId.ToString(), fileId.ToString());
                    foreach (var uri in list)
                    {
                        result.Succeed(uri, id);
                    }
                }
                else
                {
                    foreach (var uri in list)
                    {
                        result.Fail(uri, new ResourceNotFoundException($"CurseForge file {fileId} not found"));
                    }
                }
            }
        }

        // curseforge.com slug URLs resolve one SearchModsAsync each; slugs are rare in pack imports.
        foreach (var (uri, slug) in slugUris)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                var resp = await client.SearchModsAsync(null, null, null, null, slug: slug).ConfigureAwait(false);
                var mod = resp.Data.FirstOrDefault();
                if (mod is null)
                {
                    result.Fail(uri, new ResourceNotFoundException($"{slug} not found in the repository"));
                }
                else
                {
                    var (_, fileId) = ExtractReference(uri);
                    result.Succeed(uri, new PackageIdentifier(label, null, mod.Id.ToString(), fileId));
                }
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                result.Fail(uri, ex);
            }
        }

        return result.ToResolveResult();
    }

    // edge.forgecdn.net/files/{high}/{low}/filename.jar ⇒ fileId = high * 1000 + low
    private static bool TryExtractFileId(Uri uri, out uint fileId)
    {
        fileId = 0;
        var segments = uri.AbsolutePath.Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (segments is ["files", var high, var low, ..]
            && uint.TryParse(high, out var h)
            && uint.TryParse(low, out var l))
        {
            fileId = h * 1000 + l;
            return true;
        }

        return false;
    }

    // curseforge.com/minecraft/{class}/{slug} and .../{slug}/files/{fileId}
    private static (string? Slug, string? FileId) ExtractReference(Uri uri)
    {
        var segments = uri.AbsolutePath.Split('/', StringSplitOptions.RemoveEmptyEntries);
        for (var i = 0; i < segments.Length; i++)
        {
            if (segments[i] == "files" && i > 0 && i + 1 < segments.Length)
            {
                return (segments[i - 1], segments[i + 1]);
            }
        }

        return (segments.LastOrDefault(), null);
    }

    public async Task<Project> QueryAsync(ScopedProjectIdentifier id)
    {
        if (uint.TryParse(id.Identity, out var modId))
        {
            try
            {
                var mod = await client.GetModAsync(modId).ConfigureAwait(false);
                return CurseForgeHelper.ToProject(label, mod.Data);
            }
            catch (ApiException ex)
            {
                if (ex.StatusCode == HttpStatusCode.NotFound)
                {
                    throw new ResourceNotFoundException($"{id.Identity} not found in the repository");
                }

                throw;
            }
        }

        throw new FormatException($"{id.Identity} is not well formatted into modId");
    }

    public async Task<BatchResolveResult<ScopedProjectIdentifier, Project>> QueryBatchAsync(
        IEnumerable<ScopedProjectIdentifier> batch)
    {
        var batchArray = batch.ToArray();
        var successful = new Dictionary<ScopedProjectIdentifier, Project>();
        var failed = new Dictionary<ScopedProjectIdentifier, Exception>();

        var parsed = new List<(ScopedProjectIdentifier Key, uint ModId)>();
        foreach (var x in batchArray)
        {
            if (uint.TryParse(x.Identity, out var modId))
            {
                parsed.Add((x, modId));
            }
            else
            {
                failed[x] = new FormatException($"{x.Identity} is not well formatted into modId");
            }
        }

        if (parsed.Count > 0)
        {
            try
            {
                var mods = await client.GetModsAsync(new([.. parsed.Select(x => x.ModId)])).ConfigureAwait(false);
                var modById = mods.Data.ToDictionary(x => x.Id);
                foreach (var (key, modId) in parsed)
                {
                    if (modById.TryGetValue(modId, out var mod))
                    {
                        successful[key] = CurseForgeHelper.ToProject(label, mod);
                    }
                    else
                    {
                        failed[key] = new ResourceNotFoundException($"{key.Identity} not found in the repository");
                    }
                }
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                foreach (var (key, _) in parsed)
                {
                    failed[key] = ex;
                }
            }
        }

        return new(successful, failed);
    }

    public async Task<Package> ResolveAsync(ScopedPackageIdentifier id, Filter filter)
    {
        if (uint.TryParse(id.Identity, out var modId))
        {
            try
            {
                // 是否具有 Vid 都应该具有相同次数的 IO Call，以避免其中一个具有更好的性能而受到不公平的待遇
                // 做不到，LatestFiles 居然并不是最新的，CF 估计有缓存而导致数据迟滞（迟大概三四个月）
                var mod = (await client.GetModAsync(modId).ConfigureAwait(false)).Data;
                if (id.Version is not null)
                {
                    if (uint.TryParse(id.Version, out var fileId))
                    {
                        var file = mod.LatestFiles.FirstOrDefault(x => x.Id == fileId)
                                ?? (await client.GetModFileAsync(modId, fileId).ConfigureAwait(false)).Data;

                        return CurseForgeHelper.ToPackage(label, mod, file);
                    }

                    throw new FormatException($"{id.Version} is not well formatted into fileId");
                }

                {
                    // var loaderNick = CurseForgeService.LoaderIdToName(filter.Loader);
                    // // GameVersion 是游戏版本，GameVersionName 是游戏版本或加载器版本
                    // // 如果加载器过滤无效或不存在、如果非模组都将短路加载器判断
                    // // LatestFiles 基本上是各个主流版本或加载器的最新版本集合，命中率较高，除了有些会把版本 1.21.1 标记为 1.21 的模组
                    // var found = mod.LatestFiles.FirstOrDefault(x => x.SortableGameVersions.Any(y => y.GameVersion
                    //                                                               == filter.Version)
                    //                                                  && (loaderNick == null
                    //                                                   || mod.ClassId != CurseForgeService.CLASSID_MOD
                    //                                                   || x.SortableGameVersions.Any(y => y.GameVersionName
                    //                                                                == loaderNick)));
                    var file = (await client
                                     .GetModFilesAsync(modId,
                                                       filter.Version,
                                                       CurseForgeHelper.GetVersionLoaderFilter(mod.ClassId,
                                                           filter.Loader),
                                                       0,
                                                       1)
                                     .ConfigureAwait(false)).Data.FirstOrDefault();
                    if (file != null)
                    {
                        return CurseForgeHelper.ToPackage(label, mod, file);
                    }

                    throw new
                        ResourceNotFoundException($"{mod.Name} ({label}:{id.Identity}@*) has no matched version for {FormatTarget(filter)}");
                }
            }
            catch (ApiException ex)
            {
                if (ex.StatusCode == HttpStatusCode.NotFound)
                {
                    throw new
                        ResourceNotFoundException($"{id.Identity}/{id.Version ?? "*"} not found in the repository");
                }

                throw;
            }
        }

        throw new FormatException($"{id.Identity} is not well formatted into modId");
    }

    public async Task<BatchResolveResult<ScopedPackageIdentifier, Package>> ResolveBatchAsync(
        IEnumerable<ScopedPackageIdentifier> batch,
        Filter filter)
    {
        var ids = batch.ToArray();
        var knownVids = ids.Where(x => x.Version is not null).ToArray();
        var unknownVids = ids.Where(x => x.Version is null).ToArray();
        var result = new RepositoryHelper.BatchResult<ScopedPackageIdentifier, Package>();

        if (unknownVids.Length > 0)
        {
            result.Merge(await RepositoryHelper
                             .ResolveAsync(unknownVids, id => ResolveUnknownVersionAsync(id, filter))
                             .ConfigureAwait(false));
        }

        if (knownVids.Length > 0)
        {
            result.Merge(await ResolveKnownVersionsAsync(knownVids).ConfigureAwait(false));
        }

        return result.ToResolveResult();
    }

    private async Task<Package> ResolveUnknownVersionAsync(ScopedPackageIdentifier id, Filter filter)
    {
        if (!uint.TryParse(id.Identity, out var modId))
        {
            throw new FormatException($"{id.Identity} is not well formatted into modId");
        }

        var mod = (await client.GetModAsync(modId).ConfigureAwait(false)).Data;
        var file = (await client
                         .GetModFilesAsync(modId,
                                           filter.Version,
                                           CurseForgeHelper.GetVersionLoaderFilter(mod.ClassId, filter.Loader),
                                           0,
                                           1)
                         .ConfigureAwait(false)).Data.FirstOrDefault()
                ?? throw new ResourceNotFoundException(
                       $"{mod.Name} ({label}:{modId}@*) has no matched version for {FormatTarget(filter)}");
        return CurseForgeHelper.ToPackage(label, mod, file);
    }

    private async Task<RepositoryHelper.BatchResult<ScopedPackageIdentifier, Package>> ResolveKnownVersionsAsync(
        ScopedPackageIdentifier[] knownVids)
    {
        var result = new RepositoryHelper.BatchResult<ScopedPackageIdentifier, Package>();
        var parsed = new List<(ScopedPackageIdentifier Id, uint ModId, uint FileId)>();
        foreach (var id in knownVids)
        {
            if (uint.TryParse(id.Identity, out var modId) && uint.TryParse(id.Version, out var fileId))
            {
                parsed.Add((id, modId, fileId));
            }
            else
            {
                result.Fail(id,
                    new FormatException($"{id.Identity}/{id.Version} is not well formatted into modId/fileId"));
            }
        }

        if (parsed.Count > 0)
        {
            try
            {
                var modById = (await client
                                     .GetModsAsync(new([.. parsed.Select(x => x.ModId)]))
                                     .ConfigureAwait(false))
                              .Data
                              .ToDictionary(x => x.Id);
                var fileById = (await client
                                      .GetFilesAsync(new([.. parsed.Select(x => x.FileId)]))
                                      .ConfigureAwait(false))
                               .Data
                               .ToDictionary(x => x.Id);

                foreach (var (id, modId, fileId) in parsed)
                {
                    if (modById.TryGetValue(modId, out var mod)
                     && fileById.TryGetValue(fileId, out var file)
                     && file.ModId == modId)
                    {
                        result.Succeed(id, CurseForgeHelper.ToPackage(label, mod, file));
                    }
                    else
                    {
                        result.Fail(id,
                            new ResourceNotFoundException(
                                $"{id.Identity}/{id.Version} not found in the repository"));
                    }
                }
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                result.FailAll(parsed.Select(x => x.Id), ex);
            }
        }

        return result;
    }

    public async Task<string> ReadDescriptionAsync(ScopedProjectIdentifier id)
    {
        if (uint.TryParse(id.Identity, out var modId))
        {
            try
            {
                var html = (await client.GetModDescriptionAsync(modId).ConfigureAwait(false)).Data;
                return CONVERTER.Convert(html);
            }
            catch (ApiException ex)
            {
                if (ex.StatusCode == HttpStatusCode.NotFound)
                {
                    throw new ResourceNotFoundException($"{id.Identity} not found in the repository");
                }

                throw;
            }
        }

        throw new FormatException($"{id.Identity} is not well formatted into modId");
    }

    public async Task<string> ReadChangelogAsync(ScopedPackageIdentifier id)
    {
        if (uint.TryParse(id.Identity, out var modId) && uint.TryParse(id.Version, out var fileId))
        {
            try
            {
                var html = (await client.GetModFileChangelogAsync(modId, fileId).ConfigureAwait(false)).Data;
                return CONVERTER.Convert(html);
            }
            catch (ApiException ex)
            {
                if (ex.StatusCode == HttpStatusCode.NotFound)
                {
                    throw new ResourceNotFoundException($"{id.Identity}/{id.Version} not found in the repository");
                }

                throw;
            }
        }

        throw new FormatException("Pid or Vid is not well formatted into modId or fileId");
    }

    public async Task<IPaginationHandle<Version>> InspectAsync(ScopedProjectIdentifier id, Filter filter)
    {
        if (uint.TryParse(id.Identity, out var modId))
        {
            var mod = (await client.GetModAsync(modId).ConfigureAwait(false)).Data;
            var loader = CurseForgeHelper.GetVersionLoaderFilter(mod.ClassId, filter.Loader);
            var first = await client
                             .GetModFilesAsync(modId, filter.Version, loader, 0, PAGE_SIZE)
                             .ConfigureAwait(false);
            var initial = first.Data.Select(x => CurseForgeHelper.ToVersion(label, x));
            return new PaginationHandle<Version>(initial,
                                                 first.Pagination.PageSize,
                                                 first.Pagination.TotalCount,
                                                 async (pageIndex, _) =>
                                                 {
                                                     var rv = await client
                                                                   .GetModFilesAsync(modId,
                                                                        filter.Version,
                                                                        loader,
                                                                        pageIndex * first.Pagination.PageSize,
                                                                        first.Pagination.PageSize)
                                                                   .ConfigureAwait(false);
                                                     return rv.Data.Select(x => CurseForgeHelper.ToVersion(label, x));
                                                 });
        }

        throw new FormatException("Pid is not well formatted into modId");
    }

    #endregion
}
