using Spectre.Console.Cli;
using TridentCore.Cli.Operations;
using TridentCore.Cli.Services;
using TridentCore.Core.Services;

namespace TridentCore.Cli.Commands.Instance;

public class InstanceInstallCommand(
    InstanceManager instanceManager,
    RepositoryAgent repositories,
    TrackerAwaiter awaiter,
    CliOutput output
) : Command<InstanceInstallCommand.Arguments>
{
    protected override int Execute(
        CommandContext context,
        Arguments settings,
        CancellationToken cancellationToken
    )
    {
        var tracker = InstanceOperation
            .StartInstallAsync(instanceManager, repositories, settings.Pref, settings.Identity)
            .GetAwaiter()
            .GetResult();
        awaiter.AwaitInstallAsync(tracker, CancellationToken.None).GetAwaiter().GetResult();

        if (output.UseStructuredOutput)
        {
            output.WriteData(
                new { action = "install", key = tracker.Key, source = tracker.Reference }
            );
        }
        else
        {
            output.WriteKeyValueTable(
                "Modpack installed",
                ("Instance", tracker.Key),
                ("Source", tracker.Reference ?? "-")
            );
            output.WriteSuccess($"Instance {tracker.Key} installed.");
        }

        return ExitCodes.SUCCESS;
    }

    public class Arguments : CommandSettings
    {
        [CommandOption("--identity <KEY>")]
        public string? Identity { get; set; }

        [CommandArgument(0, "<PREF>")]
        public required string Pref { get; set; }
    }
}
