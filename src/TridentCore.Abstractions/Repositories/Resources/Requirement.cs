namespace TridentCore.Abstractions.Repositories.Resources;

public record Requirement(IEnumerable<string> AnyOfVersions, IEnumerable<string> AnyOfLoaders);
