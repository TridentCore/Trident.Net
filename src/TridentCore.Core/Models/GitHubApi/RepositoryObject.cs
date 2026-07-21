namespace TridentCore.Core.Models.GitHubApi;

public record RepositoryObject(string? Description, IReadOnlyList<string>? Topics);
