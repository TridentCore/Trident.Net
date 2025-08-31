namespace Trident.Core.Models.CurseForgeApi;

public readonly record struct GetFilesRequest(IReadOnlyList<uint> FileIds);
