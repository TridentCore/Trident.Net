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
             .Columns(new TaskDescriptionColumn(), new ProgressBarColumn(), new PercentageColumn(), new SpinnerColumn())
             .StartAsync(async progressContext =>
              {
                  var task = progressContext.AddTask("[blue]Preparing build[/]", maxValue: 1);
                  await AwaitCoreAsync(tracker,
                                       stage => task.Description = $"[blue]{Markup.Escape(stage)}[/]",
                                       progress =>
                                       {
                                           task.Value = Math.Clamp(progress, 0d, 1d);
                                       },
                                       cancellationToken)
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
        CancellationToken cancellationToken)
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

    public async Task AwaitInstallAsync(InstallTracker tracker, CancellationToken cancellationToken)
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
             .Columns(new TaskDescriptionColumn(), new ProgressBarColumn(), new PercentageColumn(), new SpinnerColumn())
             .StartAsync(async progressContext =>
              {
                  var task = progressContext.AddTask("[blue]Resolving modpack[/]", maxValue: 1);
                  using var subscription = MapInstallProgress(tracker.ProgressChanged)
                                          .Sample(TimeSpan.FromSeconds(1))
                                          .Subscribe(x =>
                                           {
                                               task.Description = $"[blue]{Markup.Escape(Label(x.Phase))}[/]";
                                               if (x.Percent is { } percent)
                                               {
                                                   task.Value = Math.Clamp(percent, 0d, 1d);
                                               }
                                           });
                  await AwaitCompletionAsync(tracker, cancellationToken).ConfigureAwait(false);
                  task.Value = task.MaxValue;
                  task.StopTask();
              })
             .ConfigureAwait(false);

        ThrowIfFaulted(tracker, "Install failed.");
    }

    private async Task AwaitInstallJsonAsync(InstallTracker tracker, CancellationToken cancellationToken)
    {
        using var subscription = MapInstallProgress(tracker.ProgressChanged)
                                .Sample(TimeSpan.FromSeconds(1))
                                .Subscribe(x =>
                                 {
                                     var phase = x.Phase.ToString().ToLowerInvariant();
                                     object payload = x.Percent is { } percent
                                                          ? new { @event = "progress", phase, percent }
                                                          : new { @event = "progress", phase, indeterminate = true };
                                     Console.Out.WriteLine(JsonSerializer.Serialize(payload));
                                 });
        await AwaitCompletionAsync(tracker, cancellationToken).ConfigureAwait(false);
        ThrowIfFaulted(tracker, "Install failed.");
    }

    // NOTE: InstallTracker 只发标量 double?（null=不可量化，num=下载字节比），不带阶段语义。
    // 安装流程阶段是单调的 解析→下载→解压，所以靠「首次出现 Determinate」和「Determinate 之后的 Indeterminate」
    // 两次状态跃迁把标量流重写成三阶段，免改 Core 的 Tracker 模型。
    private static IObservable<(InstallPhase Phase, double? Percent)> MapInstallProgress(
        IObservable<TrackerProgress> source)
    {
        var phase = InstallPhase.Resolving;
        return source.Select(p =>
        {
            switch (p)
            {
                case TrackerProgress.Determinate d:
                    phase = InstallPhase.Downloading;
                    return (phase, d.Percent);
                case TrackerProgress.Indeterminate when phase == InstallPhase.Downloading:
                    phase = InstallPhase.Extracting;
                    return (phase, null);
                default:
                    return (phase, (double?)null);
            }
        });
    }

    private static string Label(InstallPhase phase) =>
        phase switch
        {
            InstallPhase.Resolving => "Resolving modpack",
            InstallPhase.Downloading => "Downloading modpack",
            InstallPhase.Extracting => "Extracting modpack",
            _ => "Installing modpack"
        };

    public static void ThrowIfFaulted(TrackerBase tracker, string message)
    {
        if (tracker.State == TrackerState.Faulted)
        {
            throw tracker.FailureReason ?? new InvalidOperationException(message);
        }
    }

    public static async Task AwaitCompletionAsync(TrackerBase tracker, CancellationToken cancellationToken)
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

    private enum InstallPhase { Resolving, Downloading, Extracting }
}
