using Microsoft.Extensions.Logging;
using TridentCore.Abstractions;
using TridentCore.Core.Utilities;

namespace TridentCore.Core.Engines.Deploying.Stages;

public class LoadLaunchPlanStage(ILogger<LoadLaunchPlanStage> logger) : StageBase
{
    protected override Task OnProcessAsync(CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        if (Context.LaunchPlanSnapshot is { } snapshot)
        {
            Context.LaunchPlanDocument = snapshot.Document;
            logger.LogInformation("Loaded external launch plan from {path}", PathDef.Default.FileOfLaunchPlan(Context.Key));
        }
        else
        {
            logger.LogInformation("No external launch plan found");
        }

        return Task.CompletedTask;
    }
}
