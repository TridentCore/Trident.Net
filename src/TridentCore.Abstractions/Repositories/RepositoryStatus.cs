using TridentCore.Abstractions.Repositories.Resources;

namespace TridentCore.Abstractions.Repositories;

public record RepositoryStatus(
    IReadOnlyList<string> SupportedLoaders,
    IReadOnlyList<string> SupportedVersions,
    IReadOnlyList<ResourceKind> SupportedKinds
);
