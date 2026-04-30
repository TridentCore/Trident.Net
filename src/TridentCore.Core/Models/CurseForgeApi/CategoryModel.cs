namespace TridentCore.Core.Models.CurseForgeApi;

public record CategoryModel(
    uint Id,
    uint GameId,
    string Name,
    string Slug,
    Uri Url,
    Uri IconUrl,
    DateTimeOffset DateModified,
    bool? IsClass,
    uint? ClassId,
    uint? ParentCategoryId,
    int? DisplayIndex
);
