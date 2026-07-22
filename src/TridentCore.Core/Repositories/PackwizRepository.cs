using System.Net;
using Refit;
using TridentCore.Abstractions.Repositories;
using TridentCore.Abstractions.Repositories.Resources;
using TridentCore.Abstractions.Utilities;
using TridentCore.Core.Clients;
using TridentCore.Core.Models.GitHubApi;
using TridentCore.Core.Utilities;
using TridentCore.Pref;
using Version = TridentCore.Abstractions.Repositories.Resources.Version;

namespace TridentCore.Core.Repositories;

// A hidden repository: an entire GitHub-hosted packwiz repo is exposed as a single Modpack
// package — never listed in browse/marketplace, but resolvable by label.
public class PackwizRepository(string label, IGitHubClient github) : IRepository
{
    private const int PAGE_SIZE = 30;

    public Task<PackageIdentifier> RecognizeAsync(Uri uri, CancellationToken cancellationToken = default) =>
        throw new NotSupportedException();

    public bool IsHidden => true;

    private async Task<CommitObject> GetHeadCommitAsync(string owner, string repo)
    {
        var commits = await FetchCommitsAsync(owner, repo, 1, 1).ConfigureAwait(false);
        return commits.Count == 0
                   ? throw new ResourceNotFoundException($"GitHub {owner}/{repo} has no commits")
                   : commits[0];
    }

    private async Task<CommitObject> GetCommitByRefAsync(string owner, string repo, string gitRef)
    {
        try
        {
            return await github.GetCommitAsync(owner, repo, gitRef).ConfigureAwait(false);
        }
        catch (ApiException ex) when (ex.StatusCode == HttpStatusCode.NotFound)
        {
            throw new ResourceNotFoundException($"GitHub ref {gitRef} not found in {owner}/{repo}");
        }
    }

    private async Task<IReadOnlyList<CommitObject>> FetchCommitsAsync(string owner, string repo, int perPage, uint page)
    {
        try
        {
            return await github.GetCommitsAsync(owner, repo, perPage, page).ConfigureAwait(false);
        }
        catch (ApiException ex) when (ex.StatusCode == HttpStatusCode.NotFound)
        {
            throw new ResourceNotFoundException($"GitHub {owner}/{repo} not found");
        }
    }

    private static string RequireOwner(string? ns) =>
        ns ?? throw new ResourceNotFoundException("packwiz references require an owner namespace");

    private async Task<Dictionary<string, string>> BuildTagMapAsync(string owner, string repo)
    {
        var map = new Dictionary<string, string>();
        uint page = 1;
        while (true)
        {
            var tags = await github.GetTagsAsync(owner, repo, 100, page).ConfigureAwait(false);
            if (tags.Count == 0)
            {
                break;
            }

            foreach (var tag in tags)
            {
                if (tag.Commit?.Sha is string sha)
                {
                    map[sha] = tag.Name;
                }
            }

            if (tags.Count < 100)
            {
                break;
            }

            page++;
        }

        return map;
    }

    private static string? TagOf(IReadOnlyDictionary<string, string> tagMap, string? sha) =>
        sha is not null && tagMap.TryGetValue(sha, out var tag) ? tag : null;

    #region IRepository Members

    public Task<RepositoryStatus> CheckStatusAsync() =>
        Task.FromResult(new RepositoryStatus([
                                                 LoaderHelper.LOADERID_FABRIC,
                                                 LoaderHelper.LOADERID_FORGE,
                                                 LoaderHelper.LOADERID_NEOFORGE,
                                                 LoaderHelper.LOADERID_QUILT
                                             ],
                                             Array.Empty<string>(),
                                             [ResourceKind.Modpack]));

    public Task<IPaginationHandle<Exhibit>> SearchAsync(string query, Filter filter) =>
        throw new NotSupportedException("packwiz repositories are not searchable");

    public Task<Package> IdentifyAsync(ReadOnlyMemory<byte> content) =>
        throw new NotSupportedException("packwiz repositories cannot identify files");

    public async Task<Project> QueryAsync(ScopedProjectIdentifier id)
    {
        var owner = RequireOwner(id.Namespace);
        var head = await GetHeadCommitAsync(owner, id.Identity).ConfigureAwait(false);
        var info = await github.GetRepositoryAsync(owner, id.Identity).ConfigureAwait(false);
        var file = await github
                        .GetFileContentAsync(owner, id.Identity, PackwizHelper.INDEX_FILE_NAME, head.Sha)
                        .ConfigureAwait(false);
        var manifest = PackwizHelper.ParsePackManifest(PackwizHelper.DecodeContent(file));
        return PackwizHelper.ToProject(label,
                                       owner,
                                       id.Identity,
                                       head,
                                       manifest,
                                       info.Description ?? string.Empty,
                                       info.Topics ?? Array.Empty<string>());
    }

    public async Task<BatchResolveResult<ScopedProjectIdentifier, Project>> QueryBatchAsync(
        IEnumerable<ScopedProjectIdentifier> batch) =>
        (await RepositoryHelper
                 .ResolveAsync<ScopedProjectIdentifier, Project>(batch, QueryAsync)
                 .ConfigureAwait(false))
       .ToResolveResult();

    public async Task<Package> ResolveAsync(ScopedPackageIdentifier id, Filter filter)
    {
        var owner = RequireOwner(id.Namespace);
        var commit = id.Version is null
                         ? await GetHeadCommitAsync(owner, id.Identity).ConfigureAwait(false)
                         : await GetCommitByRefAsync(owner, id.Identity, id.Version).ConfigureAwait(false);
        var info = await github.GetRepositoryAsync(owner, id.Identity).ConfigureAwait(false);
        var file = await github
                        .GetFileContentAsync(owner, id.Identity, PackwizHelper.INDEX_FILE_NAME, commit.Sha)
                        .ConfigureAwait(false);
        var manifest = PackwizHelper.ParsePackManifest(PackwizHelper.DecodeContent(file));
        return PackwizHelper.ToPackage(label,
                                       owner,
                                       id.Identity,
                                       commit,
                                       id.Version,
                                       manifest,
                                       info.Description ?? string.Empty);
    }

    public async Task<BatchResolveResult<ScopedPackageIdentifier, Package>> ResolveBatchAsync(
        IEnumerable<ScopedPackageIdentifier> batch,
        Filter filter) =>
        (await RepositoryHelper
                 .ResolveAsync<ScopedPackageIdentifier, Package>(batch, id => ResolveAsync(id, filter))
                 .ConfigureAwait(false))
       .ToResolveResult();

    public async Task<string> ReadDescriptionAsync(ScopedProjectIdentifier id)
    {
        var owner = RequireOwner(id.Namespace);
        try
        {
            var readme = await github.GetReadmeAsync(owner, id.Identity).ConfigureAwait(false);
            return PackwizHelper.DecodeContent(readme);
        }
        catch (ApiException ex) when (ex.StatusCode == HttpStatusCode.NotFound)
        {
            return string.Empty;
        }
    }

    public async Task<string> ReadChangelogAsync(ScopedPackageIdentifier id)
    {
        if (id.Namespace is null || id.Version is null)
        {
            return string.Empty;
        }

        try
        {
            var commit = await github.GetCommitAsync(id.Namespace, id.Identity, id.Version).ConfigureAwait(false);
            return commit.Commit?.Message ?? string.Empty;
        }
        catch (ApiException ex) when (ex.StatusCode == HttpStatusCode.NotFound)
        {
            return string.Empty;
        }
    }

    public async Task<IPaginationHandle<Version>> InspectAsync(ScopedProjectIdentifier id, Filter filter)
    {
        var owner = RequireOwner(id.Namespace);
        var tagMap = await BuildTagMapAsync(owner, id.Identity).ConfigureAwait(false);
        var first = await FetchCommitsAsync(owner, id.Identity, PAGE_SIZE, 1).ConfigureAwait(false);
        var initial = first
                     .Select(c => PackwizHelper.ToVersion(label, owner, id.Identity, c, TagOf(tagMap, c.Sha)))
                     .ToList();
        return new PaginationHandle<Version>(initial,
                                             PAGE_SIZE,
                                             (uint)initial.Count,
                                             async (pageIndex, _) =>
                                             {
                                                 var page = await FetchCommitsAsync(owner,
                                                                    id.Identity,
                                                                    PAGE_SIZE,
                                                                    pageIndex + 1)
                                                               .ConfigureAwait(false);
                                                 return page
                                                       .Select(c => PackwizHelper.ToVersion(label,
                                                                   owner,
                                                                   id.Identity,
                                                                   c,
                                                                   TagOf(tagMap, c.Sha)))
                                                       .ToList();
                                             });
    }

    #endregion
}
