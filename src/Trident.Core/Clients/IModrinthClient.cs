using Refit;
using Trident.Core.Models.ModrinthApi;

namespace Trident.Core.Clients;

public interface IModrinthClient
{
    [Get("/v2/tag/game_version")]
    Task<IReadOnlyList<GameVersion>> GetGameVersionsAsync();

    [Get("/v3/tag/loader")]
    Task<IReadOnlyList<ModLoader>> GetLoadersAsync();

    [Get("/v2/tag/project_type")]
    Task<IReadOnlyList<string>> GetProjectTypesAsync();

    [Get("/v2/search")]
    Task<SearchResponse<SearchHit>> SearchAsync(
        string query,
        string facets,
        string? index = null,
        uint offset = 0,
        uint limit = 10);

    [Get("/v3/project/{projectId}")]
    Task<ProjectInfo> GetProjectAsync(string projectId);

    [Get("/v3/projects")]
    Task<IReadOnlyList<ProjectInfo>> GetMultipleProjectsAsync(string ids);


    [Get("/v3/version/{versionId}")]
    Task<VersionInfo> GetVersionAsync(string versionId);

    [Get("/v3/versions")]
    Task<IReadOnlyList<VersionInfo>> GetMultipleVersionsAsync(string ids);

    [Get("/v3/team/{teamId}/members")]
    Task<IReadOnlyList<MemberInfo>> GetTeamMembersAsync(string teamId);

    [Get("/v3/project/{projectId}/version")]
    Task<IReadOnlyList<VersionInfo>> GetProjectVersionsAsync(
        string projectId,
        string? versionType = null,
        string? loaders = null);

    [Get("/v3/version_file/{hash}")]
    Task<VersionInfo> GetVersionFromHashAsync(string hash, [Query] string algorithm = "sha1");
}
