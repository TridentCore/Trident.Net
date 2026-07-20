namespace TridentCore.Core.Models.GitHubApi;

public record GithubTag(string Name, GithubTagCommit? Commit);

public record GithubTagCommit(string? Sha);
