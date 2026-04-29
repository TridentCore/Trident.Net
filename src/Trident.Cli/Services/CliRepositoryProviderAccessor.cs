using Trident.Core.Services;

namespace Trident.Cli.Services;

public class CliRepositoryProviderAccessor(
    BuiltinRepositoryProviderAccessor builtins,
    UserRepositoryStore userRepositories
) : IRepositoryProviderAccessor
{
    public IReadOnlyList<IRepositoryProviderAccessor.ProviderProfile> Build()
    {
        var map = builtins.Build().ToDictionary(x => x.Label, StringComparer.OrdinalIgnoreCase);
        foreach (var user in userRepositories.Load())
        {
            var driver = UserRepositoryStore.ParseDriver(user.Driver);
            map[user.Label] = new(
                user.Label,
                driver,
                user.Endpoint,
                BuildAuthorizationHeader(driver, user.ApiKey),
                user.UserAgent
            );
        }

        return [.. map.Values.OrderBy(x => x.Label, StringComparer.OrdinalIgnoreCase)];
    }

    private static (string Key, string Value)? BuildAuthorizationHeader(
        IRepositoryProviderAccessor.ProviderProfile.DriverType driver,
        string? apiKey
    )
    {
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            return null;
        }

        return driver switch
        {
            IRepositoryProviderAccessor.ProviderProfile.DriverType.CurseForge => ("x-api-key", apiKey),
            IRepositoryProviderAccessor.ProviderProfile.DriverType.Modrinth => ("Authorization", apiKey),
            _ => null,
        };
    }
}
