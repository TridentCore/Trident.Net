using TridentCore.Abstractions.Utilities;

namespace TridentCore.Core.Engines.Deploying;

// NOTE: PrismLauncher 下载的压缩包内部还包了一层，因此叫 Nested。
public record BundledRuntime(
    uint Major,
    IReadOnlyList<BundledRuntime.File> Files,
    IReadOnlyList<BundledRuntime.Link> Links)
{
    #region Nested type: File

    public record File(string Path, Uri Download, FileHash Hash, bool IsExecutable);

    #endregion

    #region Nested type: Link

    public record Link(string Path, string Target);

    #endregion
}
