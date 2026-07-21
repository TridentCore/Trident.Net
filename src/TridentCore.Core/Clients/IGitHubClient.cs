using Refit;
using TridentCore.Core.Models.GitHubApi;

namespace TridentCore.Core.Clients;

public interface IGitHubClient
{
    [Get("/repos/{owner}/{repo}/commits/{gitRef}")]
    Task<CommitObject> GetCommitAsync(string owner, string repo, string gitRef);

    [Get("/repos/{owner}/{repo}/commits")]
    Task<IReadOnlyList<CommitObject>> GetCommitsAsync(
        string owner,
        string repo,
        [AliasAs("per_page")] int perPage = 30,
        uint page = 1
    );

    [Get("/repos/{owner}/{repo}/contents/{path}")]
    Task<FileContent> GetFileContentAsync(
        string owner,
        string repo,
        string path,
        [AliasAs("ref")] string? gitRef = null
    );

    [Get("/repos/{owner}/{repo}/readme")]
    Task<FileContent> GetReadmeAsync(string owner, string repo, [AliasAs("ref")] string? gitRef = null);

    [Get("/repos/{owner}/{repo}/tags")]
    Task<IReadOnlyList<GithubTag>> GetTagsAsync(
        string owner,
        string repo,
        [AliasAs("per_page")] int perPage = 100,
        uint page = 1
    );

    [Get("/repos/{owner}/{repo}")]
    Task<RepositoryObject> GetRepositoryAsync(string owner, string repo);
}
