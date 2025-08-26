namespace Trident.Core.Models.CurseForgeApi
{
    public readonly record struct ArrayResponse<T>(IReadOnlyList<T> Data, Pagination Pagination);
}
