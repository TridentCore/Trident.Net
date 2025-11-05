namespace Trident.Core.Engines.Deploying;

// PrismLauncher 里的下下来压缩包内部包了一层，因此叫 Nested
public record BundledRuntime(uint Major, IReadOnlyList<BundledRuntime.Entry> Files)
{
    public record Entry(string Path, Uri Download, string Sha1, bool IsExecutable);
}
