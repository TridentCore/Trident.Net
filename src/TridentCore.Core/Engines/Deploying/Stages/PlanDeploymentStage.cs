using System.Diagnostics;
using System.Reactive.Subjects;
using Microsoft.Extensions.Logging;
using TridentCore.Abstractions.Tasks;
using TridentCore.Core.Services;

namespace TridentCore.Core.Engines.Deploying.Stages;

public class PlanDeploymentStage(
    DeploymentPlanner planner,
    DeploymentDiffer differ,
    DeploymentIndexService indexes,
    ILogger<PlanDeploymentStage> logger) : StageBase
{
    private DeploymentPlanningStep? _step;
    private long _lastReport;
    private long _stepStarted;

    public Subject<ActivityProgress> ProgressStream { get; } = new();

    protected override async Task OnProcessAsync(CancellationToken token)
    {
        Report(DeploymentPlanningStep.CollectSources, null);
        var assets = await indexes.EnsureAssetAsync(Context.Lock.Artifact!.AssetIndex, token).ConfigureAwait(false);
        RuntimeIndex? runtime = null;
        if (Context.Lock.RuntimeMajor is { } major)
            runtime = await indexes.EnsureRuntimeAsync(major, Context.Lock.RuntimeIndex, token).ConfigureAwait(false);

        var target = planner.CreateTarget(Context.Key, Context.Lock, assets, runtime, token,
            count => Report(DeploymentPlanningStep.BuildRequirements, count));
        var plan = differ.Diff(Context.Key, target, token,
            count => Report(count is null ? DeploymentPlanningStep.ReconcileDirectories : DeploymentPlanningStep.VerifyFiles, count));
        Context.Plan = plan;
        LogStepDuration();
        logger.LogInformation("Planned {Downloads} downloads and {Operations} local file operations", plan.Downloads.Count, plan.Operations.Count);
    }

    private void Report(DeploymentPlanningStep step, (int Current, int Total)? count)
    {
        if (count is { Total: 0 }) return;
        if (_step == step && count is { Current: > 0 } value && value.Current < value.Total
            && Stopwatch.GetElapsedTime(_lastReport).TotalMilliseconds < 100) return;

        if (_step != step)
        {
            LogStepDuration();
            _step = step;
            _stepStarted = Stopwatch.GetTimestamp();
        }
        _lastReport = Stopwatch.GetTimestamp();
        var name = $"{nameof(DeploymentPlanningStep)}_{step}";
        ActivityProgress progress = count is { } items
            ? new ActivityProgress.Determinate(name, items.Current, items.Total)
            : new ActivityProgress.Indeterminate(name);
        ProgressStream.OnNext(progress);
    }

    private void LogStepDuration()
    {
        if (_step is { } step)
            logger.LogInformation("Planning step {Step} completed in {Milliseconds}ms", step,
                Stopwatch.GetElapsedTime(_stepStarted).TotalMilliseconds);
    }

    public override void Dispose()
    {
        base.Dispose();
        ProgressStream.Dispose();
    }
}
