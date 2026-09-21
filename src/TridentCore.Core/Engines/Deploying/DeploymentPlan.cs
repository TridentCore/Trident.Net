using TridentCore.Abstractions.Utilities;

namespace TridentCore.Core.Engines.Deploying;

public sealed class DeploymentPlan
{
    public List<Download> Downloads { get; } = [];
    public List<Operation> Operations { get; } = [];
    public ImportProjectionManifest ImportManifest { get; set; } = new();
    public PersistProjectionManifest PersistManifest { get; set; } = new();
    public bool NeedsManifestCommit { get; set; }

    public abstract record Operation;
    public sealed record Download(string Path, Uri Url, FileHash? Hash, bool Executable = false);
    public sealed record EnsureImportFile(string Source, string Target) : Operation;
    public sealed record RemoveBuildFile(string Path) : Operation;
    public sealed record MoveToPersist(string Source, string Target) : Operation;
    public sealed record BackportPersistFile(string Source, string Target, long ExpectedTargetLastWriteTimeUtcTicks) : Operation;
    public sealed record RemoveLink(string Path) : Operation;
    public sealed record EnsureLink(string Path, string Target, bool Directory) : Operation;
    public sealed record MakeExecutable(string Path) : Operation;
}
