using Refit;
using TridentCore.Core.Models.ModrinthApi;

namespace TridentCore.Core.Clients;

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
        [AliasAs("version_type")] string? versionType = null,
        // WARNING: must be a JSON array string `["fabric"]`. Bare or comma-separated values
        //  are silently ignored (return zero results), unlike the `ids` parameter on the
        //  bulk endpoints which accepts comma-separated input.
        string? loaders = null,
        [AliasAs("loader_fields")] string? loaderFields = null,
        [AliasAs("include_changelog")] bool includeChangelog = false,
        uint? limit = null,
        uint? offset = null);

    // NOTE: pinned to sha1 — the only call site (IdentifyAsync) hashes with SHA1. The API
    //  auto-detects by hash length when omitted, but Refit always sends this default, so a
    //  sha512 hash would 404. Change the hashing call site before passing anything else.
    [Get("/v3/version_file/{hash}")]
    Task<VersionInfo> GetVersionFromHashAsync(string hash, [Query] string algorithm = "sha1");
}
