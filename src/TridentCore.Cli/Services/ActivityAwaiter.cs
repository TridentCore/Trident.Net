using System.Reactive.Linq;
using System.Reactive.Threading.Tasks;
using System.Text.Json;
using Spectre.Console;
using TridentCore.Abstractions.Tasks;
using TridentCore.Core.Engines.Deploying;
using TridentCore.Core.Services.Instances;

namespace TridentCore.Cli.Services;

public class ActivityAwaiter(CliOutput output)
{
    public async Task AwaitDeployAsync(
        IObservable<InstanceActivity> activities,
        CancellationToken cancellationToken)
    {
        if (!output.IsInteractive || output.UseStructuredOutput)
        {
            await AwaitCoreAsync(activities, null, null, cancellationToken).ConfigureAwait(false);
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
                  await AwaitCoreAsync(activities,
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
        IObservable<InstanceActivity> activities,
        Action<string>? onStage,
        Action<double>? onProgress,
        CancellationToken cancellationToken)
    {
        DeployStage? lastStage = null;
        using var subscription = activities.OfType<InstanceActivity.Deploying>().Subscribe(x =>
        {
            if (x.CurrentStage != lastStage)
            {
                lastStage = x.CurrentStage;
                onStage?.Invoke(x.CurrentStage.ToString());
            }

            if (x.FileCount is { Total: > 0 } count)
            {
                onProgress?.Invoke((double)count.Current / count.Total);
            }
        });

        var final = await AwaitCompletionAsync(activities, cancellationToken).ConfigureAwait(false);
        ThrowIfFaulted(final, "Deploy failed.");
    }

    public async Task AwaitInstallAsync(
        IObservable<InstanceActivity> activities,
        CancellationToken cancellationToken)
    {
        if (output.UseStructuredOutput)
        {
            await AwaitInstallJsonAsync(activities, cancellationToken).ConfigureAwait(false);
            return;
        }

        if (!output.IsInteractive)
        {
            var completed = await AwaitCompletionAsync(activities, cancellationToken).ConfigureAwait(false);
            ThrowIfFaulted(completed, "Install failed.");
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
                  using var subscription = MapInstallProgress(activities)
                                          .Sample(TimeSpan.FromSeconds(1))
                                          .Subscribe(x =>
                                           {
                                               task.Description = $"[blue]{Markup.Escape(Label(x.Phase))}[/]";
                                               if (x.Percent is { } percent)
                                               {
                                                   task.Value = Math.Clamp(percent, 0d, 1d);
                                               }
                                           });
                  var completed = await AwaitCompletionAsync(activities, cancellationToken).ConfigureAwait(false);
                  task.Value = task.MaxValue;
                  task.StopTask();
                  ThrowIfFaulted(completed, "Install failed.");
              })
             .ConfigureAwait(false);
    }

    private async Task AwaitInstallJsonAsync(
        IObservable<InstanceActivity> activities,
        CancellationToken cancellationToken)
    {
        using var subscription = MapInstallProgress(activities)
                                .Sample(TimeSpan.FromSeconds(1))
                                .Subscribe(x =>
                                 {
                                     var phase = x.Phase.ToString().ToLowerInvariant();
                                     object payload = x.Percent is { } percent
                                                          ? new { @event = "progress", phase, percent }
                                                          : new { @event = "progress", phase, indeterminate = true };
                                     Console.Out.WriteLine(JsonSerializer.Serialize(payload));
                                 });
        var completed = await AwaitCompletionAsync(activities, cancellationToken).ConfigureAwait(false);
        ThrowIfFaulted(completed, "Install failed.");
    }

    // NOTE: 安装活动只报标量进度（下载字节比 / 不可量化），不带阶段语义。安装流程阶段是单调的
    //  解析→下载→解压，所以靠「首次出现 Determinate」和「Determinate 之后的 Indeterminate」
    //  两次跃迁把标量流重写成三阶段。
    private static IObservable<(InstallPhase Phase, double? Percent)> MapInstallProgress(
        IObservable<InstanceActivity> source)
    {
        var phase = InstallPhase.Resolving;
        return source.Select(activity =>
        {
            switch (activity.Progress)
            {
                case ActivityProgress.Determinate d:
                    phase = InstallPhase.Downloading;
                    return (phase, d.Percent);
                case ActivityProgress.Indeterminate when phase == InstallPhase.Downloading:
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

    public static void ThrowIfFaulted(InstanceActivity activity, string message)
    {
        if (activity.State is ActivityState.Faulted)
        {
            throw activity.FailureReason ?? new InvalidOperationException(message);
        }

        if (activity.State is ActivityState.Cancelled)
        {
            throw new OperationCanceledException(message);
        }
    }

    /// <summary>
    ///     等到活动流终结，返回终态快照。
    /// </summary>
    /// <remarks>
    ///     活动流在落终态后即 <c>OnCompleted</c>，故 <c>LastAsync</c> 拿到的必然是终态值；
    ///     无需订阅状态事件再自行解绑。
    /// </remarks>
    public static async Task<InstanceActivity> AwaitCompletionAsync(
        IObservable<InstanceActivity> activities,
        CancellationToken cancellationToken) =>
        await activities.LastAsync().ToTask(cancellationToken).ConfigureAwait(false);

    private enum InstallPhase { Resolving, Downloading, Extracting }
}
