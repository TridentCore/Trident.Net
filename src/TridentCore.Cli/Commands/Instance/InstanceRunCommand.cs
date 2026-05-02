using System.Text.Json;
using Spectre.Console;
using Spectre.Console.Cli;
using TridentCore.Abstractions.Accounts;
using TridentCore.Abstractions.Extensions;
using TridentCore.Abstractions.Tasks;
using TridentCore.Cli.Services;
using TridentCore.Core.Accounts;
using TridentCore.Core.Engines.Launching;
using TridentCore.Core.Exceptions;
using TridentCore.Core.Igniters;
using TridentCore.Core.Services;
using TridentCore.Core.Services.Instances;
using TridentCore.Core.Utilities;
using TridentProfile = TridentCore.Abstractions.FileModels.Profile;

namespace TridentCore.Cli.Commands.Instance;

public class InstanceRunCommand(
    InstanceContextResolver resolver,
    InstanceManager instanceManager,
    AccountStore accountStore,
    TrackerAwaiter trackerAwaiter,
    CliOutput output
) : InstanceCommandBase<InstanceRunCommand.Arguments>(resolver)
{
    protected override int Execute(
        CommandContext context,
        Arguments settings,
        CancellationToken cancellationToken
    )
    {
        RunAsync(settings, cancellationToken).GetAwaiter().GetResult();
        return ExitCodes.Success;
    }

    private async Task RunAsync(Arguments settings, CancellationToken cancellationToken)
    {
        var instance = ResolveInstance(settings);

        var account = ResolveAccount(settings);
        var profile = instance.Profile;
        var width =
            settings.Width ?? profile.GetOverride(TridentProfile.OVERRIDE_WINDOW_WIDTH, 1270u);
        var height =
            settings.Height ?? profile.GetOverride(TridentProfile.OVERRIDE_WINDOW_HEIGHT, 720u);

        var launchOptions = new LaunchOptions(
            launchMode: settings.Mode ?? LaunchMode.Managed,
            account: account,
            windowSize: (width, height),
            quickConnectAddress: settings.QuickConnect
                ?? profile.GetOverride<string>(TridentProfile.OVERRIDE_BEHAVIOR_CONNECT_SERVER),
            maxMemory: settings.MaxMemory
                ?? profile.GetOverride(TridentProfile.OVERRIDE_JAVA_MAX_MEMORY, 4096u),
            additionalArguments: settings.AdditionalArguments
                ?? profile.GetOverride<string>(TridentProfile.OVERRIDE_JAVA_ADDITIONAL_ARGUMENTS)
        );
        var deployOptions = new DeployOptions(
            settings.FastMode
                ?? profile.GetOverride(TridentProfile.OVERRIDE_BEHAVIOR_DEPLOY_FASTMODE, false),
            settings.ResolveDependency
                ?? profile.GetOverride(TridentProfile.OVERRIDE_BEHAVIOR_RESOLVE_DEPENDENCY, false),
            settings.FullCheck
        );

        var locator = JavaHelper.MakeLocator(
            _ =>
                settings.JavaHome ?? profile.GetOverride<string>(TridentProfile.OVERRIDE_JAVA_HOME),
            true
        );

        if (!output.UseStructuredOutput)
        {
            output.WriteKeyValueTable(
                "Run plan",
                ("Instance", instance.Key),
                ("Mode", launchOptions.Mode.ToString()),
                ("Account", account.Username),
                ("Deploy", deployOptions.FastMode ? "fast" : "full")
            );
        }

        var deployTracker = instanceManager.Deploy(instance.Key, deployOptions, locator);
        await trackerAwaiter
            .AwaitDeployAsync(deployTracker, cancellationToken)
            .ConfigureAwait(false);

        var tracker = instanceManager.Launch(instance.Key, launchOptions, locator);

        if (launchOptions.Mode == LaunchMode.Managed)
        {
            await AwaitLaunchAsync(tracker, cancellationToken).ConfigureAwait(false);
        }
        else
        {
            await AwaitFireAndForgetAsync(tracker, cancellationToken).ConfigureAwait(false);
        }
    }

    private async Task AwaitLaunchAsync(LaunchTracker tracker, CancellationToken cancellationToken)
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(
            cancellationToken,
            tracker.Token
        );

        var scrapDisposable = tracker.ScrapStream.Subscribe(scrap =>
        {
            WriteScrap(scrap);
        });

        using var _ = scrapDisposable;

        if (output.CanUseRichOutput)
        {
            await AwaitLaunchStartAsync(tracker, cancellationToken).ConfigureAwait(false);

            if (tracker.State != TrackerState.Faulted)
            {
                output.WriteSuccess($"Game process started for {tracker.Key}.");
            }
        }

        try
        {
            await AwaitLaunchCompletionAsync(tracker, cts.Token).ConfigureAwait(false);

            if (tracker.State == TrackerState.Faulted)
            {
                var ex = tracker.FailureReason;
                if (ex is ProcessFaultedException pfe)
                {
                    output.WriteError($"Game exited with code {pfe.ExitCode}.");
                    throw new CliException(
                        $"Game exited with code {pfe.ExitCode}.",
                        pfe,
                        ExitCodes.Unknown
                    );
                }

                var message = ex?.Message ?? "Launch failed.";
                output.WriteError(message);
                if (ex != null)
                {
                    throw new CliException(message, ex, ExitCodes.Unknown);
                }

                throw new CliException(message, ExitCodes.Unknown);
            }

            if (output.CanUseRichOutput)
            {
                output.WriteSuccess($"Instance {tracker.Key} exited.");
            }
        }
        finally
        {
            if (cts.IsCancellationRequested && tracker.State == TrackerState.Running)
            {
                tracker.Abort();
            }
        }
    }

    private async Task AwaitFireAndForgetAsync(
        LaunchTracker tracker,
        CancellationToken cancellationToken
    )
    {
        await output
            .StatusAsync(
                $"Launching instance {tracker.Key}...",
                () => AwaitLaunchCompletionAsync(tracker, cancellationToken)
            )
            .ConfigureAwait(false);

        if (tracker.State == TrackerState.Faulted)
        {
            var ex = tracker.FailureReason;
            var message = ex?.Message ?? "Launch failed.";
            output.WriteError(message);
            if (ex != null)
            {
                throw new CliException(message, ex, ExitCodes.Unknown);
            }

            throw new CliException(message, ExitCodes.Unknown);
        }

        if (output.UseStructuredOutput)
        {
            output.WriteData(
                new
                {
                    action = "run",
                    key = tracker.Key,
                    mode = "fire-and-forget",
                    state = "launched",
                }
            );
        }
        else
        {
            output.WriteSuccess($"Instance {tracker.Key} launched (fire-and-forget).");
        }
    }

    private async Task AwaitLaunchStartAsync(
        LaunchTracker tracker,
        CancellationToken cancellationToken
    )
    {
        if (!output.IsInteractive || output.UseStructuredOutput)
        {
            await AwaitLaunchStartCoreAsync(tracker, cancellationToken).ConfigureAwait(false);
            return;
        }

        await AnsiConsole
            .Status()
            .Spinner(Spinner.Known.Star)
            .SpinnerStyle(Style.Parse("green"))
            .StartAsync(
                $"[green]Starting game[/] [cyan]{Markup.Escape(tracker.Key)}[/]...",
                async _ =>
                    await AwaitLaunchStartCoreAsync(tracker, cancellationToken)
                        .ConfigureAwait(false)
            )
            .ConfigureAwait(false);
    }

    private static async Task AwaitLaunchStartCoreAsync(
        LaunchTracker tracker,
        CancellationToken cancellationToken
    )
    {
        using var cancelReg = cancellationToken.Register(() =>
        {
            tracker.Abort();
        });

        while (!cancellationToken.IsCancellationRequested)
        {
            if (tracker.State is TrackerState.Finished or TrackerState.Faulted)
            {
                return;
            }

            if (TryGetStartedProcessId(tracker.Process, out _))
            {
                return;
            }

            await Task.Delay(50, cancellationToken).ConfigureAwait(false);
        }

        cancellationToken.ThrowIfCancellationRequested();
    }

    private static bool TryGetStartedProcessId(
        System.Diagnostics.Process? process,
        out int processId
    )
    {
        processId = 0;
        if (process == null)
        {
            return false;
        }

        try
        {
            processId = process.Id;
            return true;
        }
        catch (InvalidOperationException)
        {
            return false;
        }
    }

    private static async Task AwaitLaunchCompletionAsync(
        LaunchTracker tracker,
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
        using var cancelReg = cancellationToken.Register(() =>
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

    private void WriteScrap(Scrap scrap)
    {
        if (output.UseStructuredOutput)
        {
            Console.Out.WriteLine(JsonSerializer.Serialize(scrap));
            return;
        }

        var (level, color) = scrap.Level switch
        {
            ScrapLevel.Error => ("ERROR", "red"),
            ScrapLevel.Warning => ("WARN", "yellow"),
            ScrapLevel.Information => ("INFO", "green"),
            _ => ("GAME", "cyan"),
        };
        var time = string.IsNullOrWhiteSpace(scrap.Time)
            ? "[dim]--:--:--[/]"
            : $"[grey]{Markup.Escape(scrap.Time)}[/]";
        var thread = string.IsNullOrWhiteSpace(scrap.Thread)
            ? "[dim]-[/]"
            : $"[dim]{Markup.Escape(scrap.Thread)}[/]";
        var sender = string.IsNullOrWhiteSpace(scrap.Sender)
            ? string.Empty
            : $" [purple]{Markup.Escape(scrap.Sender)}[/]";
        var message = Markup.Escape(scrap.Message);

        AnsiConsole.MarkupLine(
            $"{time} [{color}]{level, -5}[/] [grey]{thread}[/]{sender} {message}"
        );
    }

    private IAccount ResolveAccount(Arguments settings)
    {
        var accounts = accountStore.Load();

        if (!string.IsNullOrEmpty(settings.Account))
        {
            var match = accounts.FirstOrDefault(a =>
                string.Equals(a.Uuid, settings.Account, StringComparison.OrdinalIgnoreCase)
                || string.Equals(a.Username, settings.Account, StringComparison.OrdinalIgnoreCase)
            );

            if (match != null)
            {
                return DeserializeAccount(match);
            }

            throw new CliException(
                $"Account '{settings.Account}' not found. Use 'trident account list' to see available accounts.",
                ExitCodes.NotFound
            );
        }

        var defaultAccount = accounts.FirstOrDefault(a => a.IsDefault) ?? accounts.FirstOrDefault();

        if (defaultAccount != null)
        {
            return DeserializeAccount(defaultAccount);
        }

        if (!string.IsNullOrEmpty(settings.Username))
        {
            return new OfflineAccount
            {
                Username = settings.Username,
                Uuid = Guid.NewGuid().ToString().Replace("-", string.Empty),
            };
        }

        return new OfflineAccount
        {
            Username = "Player",
            Uuid = Guid.NewGuid().ToString().Replace("-", string.Empty),
        };
    }

    private static IAccount DeserializeAccount(StoredAccount stored)
    {
        if (stored.Type == "microsoft")
        {
            var payload = JsonSerializer.Deserialize<MicrosoftAccount>(
                stored.Data,
                AccountStore.SerializerOptions
            );
            if (payload != null)
            {
                return payload;
            }
        }

        var offline = JsonSerializer.Deserialize<StoredOfflineAccount>(
            stored.Data,
            AccountStore.SerializerOptions
        );
        if (offline != null)
        {
            return new OfflineAccount { Username = offline.Username, Uuid = offline.Uuid };
        }

        return new OfflineAccount { Username = stored.Username, Uuid = stored.Uuid };
    }

    public class Arguments : InstanceArgumentsBase
    {
        [CommandOption("--mode <MODE>")]
        public LaunchMode? Mode { get; set; }

        [CommandOption("-A|--account <IDENTIFIER>")]
        public string? Account { get; set; }

        [CommandOption("-u|--username <NAME>")]
        public string? Username { get; set; }

        [CommandOption("--java-home <PATH>")]
        public string? JavaHome { get; set; }

        [CommandOption("--max-memory <MB>")]
        public uint? MaxMemory { get; set; }

        [CommandOption("--width <PX>")]
        public uint? Width { get; set; }

        [CommandOption("--height <PX>")]
        public uint? Height { get; set; }

        [CommandOption("--quick-connect <ADDRESS>")]
        public string? QuickConnect { get; set; }

        [CommandOption("--additional-arguments <ARGS>")]
        public string? AdditionalArguments { get; set; }

        [CommandOption("--fast")]
        public bool? FastMode { get; set; }

        [CommandOption("--resolve-dependency")]
        public bool? ResolveDependency { get; set; }

        [CommandOption("--full-check")]
        public bool? FullCheck { get; set; }
    }
}
