using TridentCore.Abstractions.Repositories;

namespace TridentCore.Core.Services;

public interface IRepositoryProviderAccessor
{
    IReadOnlyList<ProviderProfile> Build();

    IReadOnlyList<ProviderCustom> BuildCustom();

    #region Nested type: ProviderProfile

    record struct ProviderProfile(
        string Label,
        ProviderProfile.DriverType Driver,
        string Endpoint,
        (string Key, string Value)? AuthorizationHeader,
        string? UserAgent
    )
    {
        #region DriverType enum

        public enum DriverType
        {
            CurseForge,
            Modrinth,
            GitHub,
        }

        #endregion
    }

    #endregion

    #region Nested type: ProviderCustom

    record struct ProviderCustom(string Label, IRepository Instance)
    {

    }
    #endregion
}
