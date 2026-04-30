using System.Text.Json;
using Spectre.Console.Cli;
using Trident.Abstractions.Accounts;
using Trident.Abstractions.Tasks;
using Trident.Cli.Services;
using Trident.Core.Accounts;
using Trident.Core.Engines.Launching;
using Trident.Core.Exceptions;
using Trident.Core.Igniters;
using Trident.Core.Services;
using Trident.Core.Services.Instances;
using Trident.Core.Utilities;

namespace Trident.Cli.Commands.Instance;

public class InstanceRunCommand(
    InstanceContextResolver resolver,
    InstanceManager instanceManager,
    AccountStore accountStore,
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

        IAccount account = settings.Mode == LaunchMode.FireAndForget
            ? ResolveAccount(settings)
            : ResolveAccount(settings);

        var launchOptions = new LaunchOptions(
            launchMode: settings.Mode ?? LaunchMode.Managed,
            account: account,
            windowSize: settings.Width.HasValue && settings.Height.HasValue
                ? (settings.Width.Value, settings.Height.Value)
                : null,
            quickConnectAddress: settings.QuickConnect,
            maxMemory: settings.MaxMemory ?? 4096,
            additionalArguments: settings.AdditionalArguments
        );

        var locator = JavaHelper.MakeLocator(_ => settings.JavaHome, true);

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

    private async Task AwaitLaunchAsync(
        LaunchTracker tracker,
        CancellationToken cancellationToken
    )
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(
            cancellationToken,
            tracker.Token
        );

        if (output.CanUseRichOutput)
        {
            output.WriteInfo($"Launching instance {tracker.Key}...");
        }

        var scrapDisposable = tracker.ScrapStream.Subscribe(scrap =>
        {
            WriteScrap(scrap);
        });

        using var _ = scrapDisposable;

        var tcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        void OnStateUpdated(TrackerBase sender, TrackerState state)
        {
            if (state is TrackerState.Finished or TrackerState.Faulted)
            {
                tcs.TrySetResult();
            }
        }

        tracker.StateUpdated += OnStateUpdated;
        using var cancelReg = cts.Token.Register(() => tcs.TrySetCanceled(cts.Token));

        try
        {
            await tcs.Task.ConfigureAwait(false);

            if (tracker.State == TrackerState.Faulted)
            {
                var ex = tracker.FailureReason;
                if (ex is ProcessFaultedException pfe)
                {
                    output.WriteError($"Game exited with code {pfe.ExitCode}.");
                    throw new CliException($"Game exited with code {pfe.ExitCode}.", ExitCodes.Unknown);
                }

                output.WriteError(ex?.Message ?? "Launch failed.");
                throw new CliException(ex?.Message ?? "Launch failed.", ExitCodes.Unknown);
            }

            if (output.CanUseRichOutput)
            {
                output.WriteSuccess($"Instance {tracker.Key} exited.");
            }
        }
        finally
        {
            tracker.StateUpdated -= OnStateUpdated;
        }
    }

    private async Task AwaitFireAndForgetAsync(
        LaunchTracker tracker,
        CancellationToken cancellationToken
    )
    {
        var tcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        void OnStateUpdated(TrackerBase sender, TrackerState state)
        {
            if (state is TrackerState.Finished or TrackerState.Faulted)
            {
                tcs.TrySetResult();
            }
        }

        tracker.StateUpdated += OnStateUpdated;
        using var cancelReg = cancellationToken.Register(() => tcs.TrySetCanceled(cancellationToken));

        try
        {
            await tcs.Task.ConfigureAwait(false);

            if (tracker.State == TrackerState.Faulted)
            {
                var ex = tracker.FailureReason;
                output.WriteError(ex?.Message ?? "Launch failed.");
                throw new CliException(ex?.Message ?? "Launch failed.", ExitCodes.Unknown);
            }

            if (output.UseStructuredOutput)
            {
                output.WriteData(
                    new { action = "run", key = tracker.Key, mode = "fire-and-forget", state = "launched" }
                );
            }
            else
            {
                output.WriteSuccess($"Instance {tracker.Key} launched (fire-and-forget).");
            }
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

        var message = scrap.Sender != null
            ? $"[{scrap.Sender}] {scrap.Message}"
            : scrap.Message;

        if (scrap.Thread != null && scrap.Time != null)
        {
            message = $"{scrap.Time} [{scrap.Thread}] {message}";
        }

        switch (scrap.Level)
        {
            case ScrapLevel.Error:
                output.WriteError(message);
                break;
            case ScrapLevel.Warning:
                output.WriteWarning(message);
                break;
            default:
                Console.Out.WriteLine(message);
                break;
        }
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

        var defaultAccount = accounts.FirstOrDefault(a => a.IsDefault)
            ?? accounts.FirstOrDefault();

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
            var payload = JsonSerializer.Deserialize<MicrosoftAccount>(stored.Data);
            if (payload != null)
            {
                return payload;
            }
        }

        var offline = JsonSerializer.Deserialize<StoredOfflineAccount>(stored.Data);
        if (offline != null)
        {
            return new OfflineAccount
            {
                Username = offline.Username,
                Uuid = offline.Uuid,
            };
        }

        return new OfflineAccount
        {
            Username = stored.Username,
            Uuid = stored.Uuid,
        };
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
    }
}
