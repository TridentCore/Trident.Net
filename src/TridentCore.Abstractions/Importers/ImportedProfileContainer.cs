using TridentCore.Abstractions.FileModels;

namespace TridentCore.Abstractions.Importers;

public record ImportedProfileContainer(
    Profile Profile,
    IReadOnlyList<(string Source, string Target)> ImportFileNames,
    IReadOnlyList<(string Source, string Target)> HomeFileNames,
    Uri? IconUrl)
{
    public PatchesData Patches { get; init; } = new();

    #region Nested type: PatchesData

    /// <summary>
    ///     归档携带的原生 patch 数据：既有的是包内源文件到 import 层的映射，另有导入器在读取过程中
    ///     就地生成的文档（例如由外部启动器格式翻译而来），后者直接以字节形式给出。
    /// </summary>
    public record PatchesData
    {
        public IReadOnlyList<(string Source, string Target)> Files { get; init; } = [];
        public IReadOnlyDictionary<string, byte[]> Generated { get; init; } = new Dictionary<string, byte[]>();
    }

    #endregion
}
