using System.Reactive.Linq;
using System.Text.Json;
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
                        progress =>
                        {
                            task.Value = Math.Clamp(progress, 0d, 1d);
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
        Action<double>? onProgress,
        CancellationToken cancellationToken
    )
    {
        using var stageSubscription = tracker.StageStream.Subscribe(stage =>
        {
            onStage?.Invoke(stage.ToString());
        });
        using var progressSubscription = tracker.ProgressStream.Subscribe(x =>
        {
            onProgress?.Invoke((double)x.Current / x.Total);
        });

        await AwaitCompletionAsync(tracker, cancellationToken).ConfigureAwait(false);
        if (tracker.State == TrackerState.Faulted)
        {
            throw tracker.FailureReason ?? new InvalidOperationException("Deploy failed.");
        }
    }

    public async Task AwaitInstallAsync(
        InstallTracker tracker,
        CancellationToken cancellationToken
    )
    {
        if (output.UseStructuredOutput)
        {
            await AwaitInstallJsonAsync(tracker, cancellationToken).ConfigureAwait(false);
            return;
        }

        if (!output.IsInteractive)
        {
            await AwaitCompletionAsync(tracker, cancellationToken).ConfigureAwait(false);
            ThrowIfFaulted(tracker, "Install failed.");
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
                var task = progressContext.AddTask("[blue]Installing modpack[/]", maxValue: 1);
                using var subscription = tracker.ProgressChanged
                    .Sample(TimeSpan.FromSeconds(1))
                    .Subscribe(p =>
                    {
                        if (p is TrackerProgress.Determinate d)
                        {
                            task.Value = Math.Clamp(d.Percent, 0d, 1d);
                        }
                    });
                await AwaitCompletionAsync(tracker, cancellationToken).ConfigureAwait(false);
                task.Value = task.MaxValue;
                task.StopTask();
            })
            .ConfigureAwait(false);

        ThrowIfFaulted(tracker, "Install failed.");
    }

    private async Task AwaitInstallJsonAsync(
        InstallTracker tracker,
        CancellationToken cancellationToken
    )
    {
        using var subscription = tracker.ProgressChanged
            .Sample(TimeSpan.FromSeconds(1))
            .Subscribe(p =>
            {
                var payload = p switch
                {
                    TrackerProgress.Determinate d => (object)new { @event = "progress", percent = d.Percent },
                    TrackerProgress.Indeterminate => new { @event = "progress", indeterminate = true },
                    _ => new { @event = "progress" },
                };
                Console.Out.WriteLine(JsonSerializer.Serialize(payload));
            });
        await AwaitCompletionAsync(tracker, cancellationToken).ConfigureAwait(false);
        ThrowIfFaulted(tracker, "Install failed.");
    }

    public static void ThrowIfFaulted(TrackerBase tracker, string message)
    {
        if (tracker.State == TrackerState.Faulted)
        {
            throw tracker.FailureReason ?? new InvalidOperationException(message);
        }
    }

    public static async Task AwaitCompletionAsync(
        TrackerBase tracker,
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
        }
        finally
        {
            tracker.StateUpdated -= OnStateUpdated;
        }
    }
}
