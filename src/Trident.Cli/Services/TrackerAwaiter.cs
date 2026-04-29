using Spectre.Console;
using Trident.Abstractions.Tasks;
using Trident.Core.Services.Instances;

namespace Trident.Cli.Services;

public class TrackerAwaiter(CliOutput output)
{
    public async Task AwaitDeployAsync(DeployTracker tracker, CancellationToken cancellationToken)
    {
        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        void OnStateUpdated(TrackerBase sender, TrackerState state)
        {
            if (state is TrackerState.Finished or TrackerState.Faulted)
            {
                completion.TrySetResult();
            }
        }

        tracker.StateUpdated += OnStateUpdated;
        using var stageSubscription = tracker.StageStream.Subscribe(stage =>
        {
            if (!output.UseStructuredOutput)
            {
                AnsiConsole.MarkupLine($"Deploy stage: [blue]{stage}[/]");
            }
        });
        using var progressSubscription = tracker.ProgressStream.Subscribe(progress =>
        {
            if (!output.UseStructuredOutput)
            {
                AnsiConsole.MarkupLine($"Resolve packages: {progress.Item1}/{progress.Item2}");
            }
        });
        using var cancellation = cancellationToken.Register(() =>
        {
            tracker.Abort();
            completion.TrySetCanceled(cancellationToken);
        });

        try
        {
            if (tracker.State is TrackerState.Finished or TrackerState.Faulted)
            {
                completion.TrySetResult();
            }

            await completion.Task.ConfigureAwait(false);
            if (tracker.State == TrackerState.Faulted)
            {
                throw tracker.FailureReason ?? new InvalidOperationException("Deploy failed.");
            }
        }
        finally
        {
            tracker.StateUpdated -= OnStateUpdated;
        }
    }
}
