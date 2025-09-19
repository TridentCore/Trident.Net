namespace Trident.Abstractions.Repositories.Resources;

public record Dependency(string Label, string? Namespace, string ProjectId, string? VersionId, bool IsRequired);
