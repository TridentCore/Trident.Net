namespace Trident.Core.Models.CurseForgeApi;

public record GetModsRequest(IReadOnlyList<uint> ModIds, bool FilterPcOnly = true);
