namespace Trident.Abstractions.FileModels;

// 打包器配置
public record PackData(IReadOnlyList<PackData.Entry> ExternalFiles, IReadOnlyList<PackData.Entry> ExternalOverrides)
{
    #region Nested type: Entry

    public record Entry(bool Enabled, string Key);

    #endregion
}
