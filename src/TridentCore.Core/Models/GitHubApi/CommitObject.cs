namespace TridentCore.Core.Models.GitHubApi;

public record CommitObject(string? Sha, CommitInner? Commit);
