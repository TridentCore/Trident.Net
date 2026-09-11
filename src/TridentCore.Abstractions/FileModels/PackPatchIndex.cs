namespace TridentCore.Abstractions.FileModels;

// 整合包内的 patch 索引：只列导入层条目。用户层属于本机用户，任何格式的打包都不携带，导入与更新也不读取。
public record PackPatchIndex
{
    public int Format { get; init; } = 1;
    public IReadOnlyList<PatchIndex.Entry> Import { get; init; } = [];
}
