using TridentCore.Abstractions.Repositories.Resources;

namespace TridentCore.Abstractions.Repositories;

public record Filter(string? Version, string? Loader, ResourceKind? Kind, ReleaseType? Type)
{
    public static readonly Filter None = new(null, null, null, null);
}
