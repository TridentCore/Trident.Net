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
        [AliasAs("page")] uint page = 1
    );

    [Get("/repos/{owner}/{repo}/contents/{path}")]
    Task<FileContent> GetFileContentAsync(
        string owner,
        string repo,
        string path,
        [AliasAs("ref")] string? gitRef = null
    );
}
