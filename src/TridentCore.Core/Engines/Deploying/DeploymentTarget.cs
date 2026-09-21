using TridentCore.Abstractions.Utilities;

namespace TridentCore.Core.Engines.Deploying;

public sealed class DeploymentTarget
{
    public List<DeploymentFileRequirement> Requirements { get; } = [];
    public List<Projection> Projections { get; } = [];

    // NOTE: 枚举数值即投影优先级，值越大优先级越高——Select 按数值降序仲裁覆盖与冲突，跨种类靠数值大小决定谁被丢弃；
    //  调整数值或插入新种类会静默改变 persist > import > package 的覆盖关系。
    public enum ProjectionKind { Package = 0, Import = 1, Persist = 2 }

    public sealed record Projection(
        string Source,
        string Target,
        ProjectionKind Kind,
        bool Directory,
        Uri? Url,
        FileHash? Hash);
}
