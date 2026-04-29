using Trident.Core.Services;
using Trident.Core.Utilities;

namespace Trident.Cli.Services;

public class BuiltinRepositoryProviderAccessor : IRepositoryProviderAccessor
{
    public IReadOnlyList<IRepositoryProviderAccessor.ProviderProfile> Build()
    {
        var curseforge = new IRepositoryProviderAccessor.ProviderProfile(
            CurseForgeHelper.LABEL,
            IRepositoryProviderAccessor.ProviderProfile.DriverType.CurseForge,
            CurseForgeHelper.ENDPOINT,
            ("x-api-key", CurseForgeHelper.API_KEY),
            null
        );

        var modrinth = new IRepositoryProviderAccessor.ProviderProfile(
            ModrinthHelper.LABEL,
            IRepositoryProviderAccessor.ProviderProfile.DriverType.Modrinth,
            ModrinthHelper.OFFICIAL_ENDPOINT,
            null,
            null
        );

        return [curseforge, modrinth];
    }
}
