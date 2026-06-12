using Spectre.Console.Cli;
using TridentCore.Cli.Operations;
using TridentCore.Cli.Services;
using TridentCore.Core.Services;

namespace TridentCore.Cli.Commands;

public class CreateCommand(ProfileManager profileManager, CliOutput output)
    : CreationCommandBase<CreateCommand.Arguments>
{
    protected override int Execute(
        CommandContext context,
        Arguments settings,
        CancellationToken cancellationToken
    )
    {
        var result = InstanceOperation.Create(
            profileManager,
            settings.Name,
            settings.Version,
            settings.Loader,
            settings.EffectiveIdentity
        );

        if (output.UseStructuredOutput)
        {
            output.WriteData(result);
        }
        else
        {
            output.WriteKeyValueTable(
                "Instance created",
                ("Key", result.Key),
                ("Name", result.Name),
                ("Version", result.Version),
                ("Loader", result.Loader)
            );
            output.WriteSuccess($"Instance {result.Key} created.");
        }

        return 0;
    }

    public class Arguments : CreationArgumentsBase
    {
        [CommandOption("-v|--version <VERSION>", true)]
        public required string Version { get; set; }

        [CommandOption("-l|--loader <LURL>")]
        public string? Loader { get; set; }
    }
}
