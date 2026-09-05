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
            logger.LogInformation("Loaded {count} external launch plan documents", snapshot.Entries.Count);
        }
        else
        {
            logger.LogInformation("No external launch plan found");
        }

        return Task.CompletedTask;
    }
}
