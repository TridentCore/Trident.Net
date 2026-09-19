using TridentCore.Core.Utilities;

namespace TridentCore.Core.Engines.Deploying.Stages;

public class SelectRuntimeStage : StageBase
{
    protected override Task OnProcessAsync(CancellationToken token)
    {
        Context.Lock = Context.Lock with
        {
            RuntimeMajor = JavaHelper.SelectRuntimeMajor(Context.Lock.Artifact!.CompatibleJavaMajors)
        };
        return Task.CompletedTask;
    }
}
