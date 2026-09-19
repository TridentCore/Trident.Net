using TridentCore.Abstractions;
using TridentCore.Core.Utilities;

namespace TridentCore.Core.Engines.Deploying;

public sealed class RuntimePlanner
{
    public DeploymentPlan Plan(RuntimeIndex index, CancellationToken token = default)
    {
        var plan = new DeploymentPlan();
        var root = PathDef.Default.DirectoryOfRuntime(index.Major);
        foreach (var file in index.Files)
        {
            token.ThrowIfCancellationRequested();
            FilePlanningHelper.RequireFile(plan, PatchHelper.ResolvePath(root, file.Path), file.Download, file.Hash, file.Executable);
        }
        return plan;
    }
}
