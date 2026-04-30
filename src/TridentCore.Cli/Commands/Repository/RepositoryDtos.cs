using TridentCore.Core.Services;

namespace TridentCore.Cli.Commands.Repository;

internal static class RepositoryDtos
{
    public static RepositoryProfileDto FromProvider(
        IRepositoryProviderAccessor.ProviderProfile profile,
        bool userDefined
    ) =>
        new(
            profile.Label,
            profile.Driver.ToString(),
            profile.Endpoint,
            userDefined,
            profile.AuthorizationHeader is not null,
            profile.UserAgent
        );
}

internal sealed record RepositoryProfileDto(
    string Label,
    string Driver,
    string Endpoint,
    bool UserDefined,
    bool HasAuthorization,
    string? UserAgent
);
