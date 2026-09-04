using Microsoft.Extensions.Logging;
using TridentCore.Abstractions.FileModels;

namespace TridentCore.Core.Engines.Deploying.Stages;

public class ResolveLaunchPlanStage(ILogger<ResolveLaunchPlanStage> logger) : StageBase
{
    protected override Task OnProcessAsync(CancellationToken token)
    {
        token.ThrowIfCancellationRequested();

        var plan = Context.LaunchPlan
                 ?? throw new InvalidOperationException("Launch plan missing before resolution");
        if (!Context.CanReuseLaunchPlan && Context.LaunchPlanDocument is { } document)
        {
            plan.Apply(document);
        }

        var result = plan.Resolve();
        Context.Lock = Context.Lock with { LaunchPlan = result };
        logger.LogInformation("Resolved launch plan with {libraries} libraries and {jvmArguments} JVM arguments",
                              result.Libraries.Length,
                              result.JavaArguments.Length);
        return Task.CompletedTask;
    }
}
