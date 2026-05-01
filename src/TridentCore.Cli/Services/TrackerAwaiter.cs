using Spectre.Console;
using TridentCore.Abstractions.Tasks;
using TridentCore.Core.Services.Instances;

namespace TridentCore.Cli.Services;

public class TrackerAwaiter(CliOutput output)
{
    public async Task AwaitDeployAsync(DeployTracker tracker, CancellationToken cancellationToken)
    {
        if (!output.IsInteractive || output.UseStructuredOutput)
        {
            await AwaitCoreAsync(tracker, null, null, cancellationToken).ConfigureAwait(false);
            return;
        }

        await AnsiConsole
            .Progress()
            .AutoClear(false)
            .HideCompleted(false)
            .Columns(
                new TaskDescriptionColumn(),
                new ProgressBarColumn(),
                new PercentageColumn(),
                new SpinnerColumn()
            )
            .StartAsync(async progressContext =>
            {
                var task = progressContext.AddTask("[blue]Preparing build[/]", maxValue: 1);
                await AwaitCoreAsync(
                        tracker,
                        stage => task.Description = $"[blue]{Markup.Escape(stage)}[/]",
                        (current, total) =>
                        {
                            task.MaxValue = Math.Max(1, total);
                            task.Value = Math.Min(current, task.MaxValue);
                        },
                        cancellationToken
                    )
                    .ConfigureAwait(false);
                task.Value = task.MaxValue;
                task.StopTask();
            })
            .ConfigureAwait(false);
    }

    private static async Task AwaitCoreAsync(
        DeployTracker tracker,
        Action<string>? onStage,
        Action<int, int>? onProgress,
        CancellationToken cancellationToken
    )
    {
        var completion = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously
        );

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
            onStage?.Invoke(stage.ToString());
        });
        using var progressSubscription = tracker.ProgressStream.Subscribe(progress =>
        {
            onProgress?.Invoke(progress.Item1, progress.Item2);
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
