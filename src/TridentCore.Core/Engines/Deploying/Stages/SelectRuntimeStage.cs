using TridentCore.Core.Services;
using TridentCore.Core.Utilities;

namespace TridentCore.Core.Engines.Deploying.Stages;

public class SelectRuntimeStage(DeploymentIndexService indexes) : StageBase
{
    protected override async Task OnProcessAsync(CancellationToken token)
    {
        var major = JavaHelper.SelectRuntimeMajor(Context.Lock.Artifact!.CompatibleJavaMajors);
        var reference = major is { } selected
            ? await indexes.ResolveRuntimeReferenceAsync(selected,
                Context.BaseLock?.RuntimeMajor == selected ? Context.BaseLock.RuntimeIndex : null, token).ConfigureAwait(false)
            : null;
        Context.Lock = Context.Lock with
        {
            RuntimeMajor = major,
            RuntimeIndex = reference
        };
    }
}
