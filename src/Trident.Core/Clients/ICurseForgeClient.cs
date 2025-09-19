using Refit;
using Trident.Core.Models.CurseForgeApi;
using Trident.Core.Utilities;
using FileInfo = Trident.Core.Models.CurseForgeApi.FileInfo;

namespace Trident.Core.Clients;

public interface ICurseForgeClient
{
    [Get("/v1/minecraft/version")]
    Task<ArrayResponse<GameVersion>> GetMinecraftVersionsAsync(bool? sortDescending = null);

    [Get("/v1/mods/search")]
    Task<SearchResponse<ModInfo>> SearchModsAsync(
        string searchFilter,
        uint? classId,
        string? gameVersion,
        ModLoaderTypeModel? modLoaderType,
        string sortOrder = "desc",
        uint index = 0,
        uint pageSize = 50,
        uint gameId = CurseForgeHelper.GAME_ID);

    [Get("/v1/mods/{modId}")]
    Task<ObjectResponse<ModInfo>> GetModAsync(uint modId);

    [Post("/v1/mods")]
    Task<ArrayResponse<ModInfo>> GetModsAsync([Body] GetModsRequest request);

    [Post("/v1/mods/files")]
    Task<ArrayResponse<FileInfo>> GetFilesAsync([Body] GetFilesRequest request);

    [Get("/v1/mods/{modId}/files/{fileId}")]
    Task<ObjectResponse<FileInfo>> GetModFileAsync(uint modId, uint fileId);

    [Get("/v1/mods/{modId}/files")]
    Task<ArrayResponse<FileInfo>> GetModFilesAsync(
        uint modId,
        string? gameVersion,
        ModLoaderTypeModel? modLoaderType,
        uint? index,
        uint? pageSize);

    [Get("/v1/mods/{modId}/description")]
    Task<ObjectResponse<string>> GetModDescriptionAsync(uint modId);

    [Get("/v1/mods/{modId}/files/{fileId}/changelog")]
    Task<ObjectResponse<string>> GetModFileChangelogAsync(uint modId, uint fileId);

    [Post("/v1/fingerprints/{gameId}")]
    Task<ObjectResponse<FingerprintMatches>> GetFingerprintMatchesByGameId(
        [Body] GetFingerprintMatchesRequest request,
        uint gameId = CurseForgeHelper.GAME_ID);
}
