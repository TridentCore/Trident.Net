using TridentCore.Abstractions.Utilities;

namespace TridentCore.Core.Engines.Deploying;

public sealed class DeploymentPlan
{
    public List<Download> Downloads { get; } = [];
    public List<Operation> Operations { get; } = [];

    public abstract record Operation;
    public sealed record Download(string Path, Uri Url, FileHash? Hash, bool Executable = false) : Operation;
    public sealed record CreateDirectory(string Path) : Operation;
    public sealed record Copy(string Source, string Target) : Operation;
    public sealed record Move(string Source, string Target) : Operation;
    public sealed record RemoveLink(string Path, bool Directory) : Operation;
    public sealed record RemoveDirectory(string Path) : Operation;
    public sealed record Link(string Path, string Target, bool Directory) : Operation;
    public sealed record MakeExecutable(string Path) : Operation;
}
