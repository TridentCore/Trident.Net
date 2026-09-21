using TridentCore.Abstractions;
using TridentCore.Core.Utilities;

namespace TridentCore.Core.Engines.Deploying;

public sealed class RuntimePlanner
{
    public void Plan(DeploymentTarget target, RuntimeIndex index, CancellationToken token = default)
    {
        var root = PathDef.Default.DirectoryOfRuntime(index.Major);
        foreach (var file in index.Files)
        {
            token.ThrowIfCancellationRequested();
            DeploymentFileHelper.RequireFile(target, PatchHelper.ResolvePath(root, file.Path), file.Download, file.Hash, file.Executable);
        }
    }
}
