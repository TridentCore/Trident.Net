namespace Trident.Core.Models.CurseForgeApi;

public readonly record struct GetModsRequest(IReadOnlyList<uint> ModIds, bool FilterPcOnly = true);
