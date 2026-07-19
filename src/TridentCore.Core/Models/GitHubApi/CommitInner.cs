namespace TridentCore.Core.Models.GitHubApi;

public record CommitInner(CommitAuthor? Committer, string? Message);
