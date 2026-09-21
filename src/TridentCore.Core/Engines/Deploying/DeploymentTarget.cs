using TridentCore.Abstractions.Utilities;

namespace TridentCore.Core.Engines.Deploying;

public sealed class DeploymentTarget
{
    public List<DeploymentPlan.Download> Downloads { get; } = [];
    public List<DeploymentPlan.Operation> PreparationOperations { get; } = [];
    public List<Projection> Projections { get; } = [];

    public enum ProjectionKind { Package = 0, Import = 1, Persist = 2 }

    public sealed record Projection(
        string Source,
        string Target,
        ProjectionKind Kind,
        bool Directory,
        Uri? Url,
        FileHash? Hash);
}
