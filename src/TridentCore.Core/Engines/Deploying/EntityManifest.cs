using TridentCore.Abstractions.Utilities;

namespace TridentCore.Core.Engines.Deploying;

public class EntityManifest
{
    // NOTE: 文件分类——Fragile：TargetPath+Url+Hash，下载到 Path 后软连到 TargetPath；
    //  Persistent：Path+TargetPath，复制 Path 到 TargetPath，IsPhantom 只建软连接；
    //  Present：Path+Url+Hash，下载到 Path；Explosive：解压到目标目录，IsDestructive 清空其余文件。

    public IList<FragileFile> FragileFiles { get; } = new List<FragileFile>();
    public IList<PersistentFile> PersistentFiles { get; } = new List<PersistentFile>();
    public IList<PresentFile> PresentFiles { get; } = new List<PresentFile>();
    public IList<ExplosiveFile> ExplosiveFiles { get; } = new List<ExplosiveFile>();

    #region Nested type: ExplosiveFile

    public record ExplosiveFile(string SourcePath, string TargetDirectory, bool Unwrap = false);

    #endregion

    #region Nested type: FragileFile

    public record FragileFile(string SourcePath, string TargetPath, Uri Url, FileHash? Hash);

    #endregion

    #region Nested type: PersistentFile

    public record PersistentFile(string SourcePath, string TargetPath, bool IsPhantom, bool IsDirectory);

    #endregion

    #region Nested type: PresentFile

    public record PresentFile(string Path, Uri Url, FileHash? Hash, bool IsExecutable = false);

    #endregion
}
