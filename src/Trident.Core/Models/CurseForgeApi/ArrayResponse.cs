namespace Trident.Core.Models.CurseForgeApi;

public record ArrayResponse<T>(IReadOnlyList<T> Data, Pagination Pagination);
