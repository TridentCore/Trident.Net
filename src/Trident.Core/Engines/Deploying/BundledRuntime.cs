namespace Trident.Core.Engines.Deploying;

// PrismLauncher 里的下下来压缩包内部包了一层，因此叫 Nested
public record BundledRuntime(
    uint Major,
    IReadOnlyList<BundledRuntime.File> Files,
    IReadOnlyList<BundledRuntime.Link> Links)
{
    #region Nested type: File

    public record File(string Path, Uri Download, string Sha1, bool IsExecutable);

    #endregion

    #region Nested type: Link

    public record Link(string Path, string Target);

    #endregion
}
