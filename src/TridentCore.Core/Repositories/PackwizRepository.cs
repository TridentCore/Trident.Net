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

    public bool IsHidden => true;

    #region IRepository Members

    public Task<RepositoryStatus> CheckStatusAsync() =>
        Task.FromResult(
            new RepositoryStatus(
                [
                    LoaderHelper.LOADERID_FABRIC,
                    LoaderHelper.LOADERID_FORGE,
                    LoaderHelper.LOADERID_NEOFORGE,
                    LoaderHelper.LOADERID_QUILT,
                ],
                Array.Empty<string>(),
                [ResourceKind.Modpack]
            )
        );

    public Task<IPaginationHandle<Exhibit>> SearchAsync(string query, Filter filter) =>
        throw new NotSupportedException("packwiz repositories are not searchable");

    public Task<Package> IdentifyAsync(ReadOnlyMemory<byte> content) =>
        throw new NotSupportedException("packwiz repositories cannot identify files");

    public async Task<Project> QueryAsync(string? ns, string pid)
    {
        var owner = RequireOwner(ns);
        var head = await GetHeadCommitAsync(owner, pid).ConfigureAwait(false);
        var file = await github
            .GetFileContentAsync(owner, pid, PackwizHelper.INDEX_FILE_NAME, head.Sha)
            .ConfigureAwait(false);
        var manifest = PackwizHelper.ParsePackManifest(PackwizHelper.DecodeContent(file));
        return PackwizHelper.ToProject(label, owner, pid, head, manifest);
    }

    public Task<BatchResolveResult<(string?, string pid), Project>> QueryBatchAsync(
        IEnumerable<(string?, string pid)> batch
    ) => throw new NotSupportedException();

    public async Task<Package> ResolveAsync(string? ns, string pid, string? vid, Filter filter)
    {
        var owner = RequireOwner(ns);
        var commit = vid is null
            ? await GetHeadCommitAsync(owner, pid).ConfigureAwait(false)
            : await GetCommitByRefAsync(owner, pid, vid).ConfigureAwait(false);
        return PackwizHelper.ToPackage(label, owner, pid, commit, vid);
    }

    public Task<BatchResolveResult<ScopedPackageIdentifier, Package>> ResolveBatchAsync(
        IEnumerable<ScopedPackageIdentifier> batch,
        Filter filter
    ) => throw new NotSupportedException();

    public Task<string> ReadDescriptionAsync(string? ns, string pid) =>
        Task.FromResult(string.Empty);

    public async Task<string> ReadChangelogAsync(string? ns, string pid, string vid)
    {
        if (ns is null || vid is null)
            return string.Empty;

        try
        {
            var commit = await github.GetCommitAsync(ns, pid, vid).ConfigureAwait(false);
            return commit.Commit?.Message ?? string.Empty;
        }
        catch (ApiException ex) when (ex.StatusCode == HttpStatusCode.NotFound)
        {
            return string.Empty;
        }
    }

    public async Task<IPaginationHandle<Version>> InspectAsync(string? ns, string pid, Filter filter)
    {
        var owner = RequireOwner(ns);
        var tagMap = await BuildTagMapAsync(owner, pid).ConfigureAwait(false);
        var first = await FetchCommitsAsync(owner, pid, PAGE_SIZE, 1).ConfigureAwait(false);
        var initial = first
            .Select(c => PackwizHelper.ToVersion(label, owner, pid, c, TagOf(tagMap, c.Sha)))
            .ToList();
        return new PaginationHandle<Version>(
            initial,
            PAGE_SIZE,
            (uint)initial.Count,
            async (pageIndex, _) =>
            {
                var page = await FetchCommitsAsync(owner, pid, PAGE_SIZE, pageIndex + 1)
                    .ConfigureAwait(false);
                return page
                    .Select(c => PackwizHelper.ToVersion(label, owner, pid, c, TagOf(tagMap, c.Sha)))
                    .ToList();
            }
        );
    }

    #endregion

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

    private async Task<IReadOnlyList<CommitObject>> FetchCommitsAsync(
        string owner,
        string repo,
        int perPage,
        uint page
    )
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
}
