using Spectre.Console.Cli;
using TridentCore.Cli.Operations;
using TridentCore.Cli.Services;
using TridentCore.Core.Services;

namespace TridentCore.Cli.Commands.Instance;

public class InstanceImportCommand(
    ProfileManager profileManager,
    ImporterAgent importerAgent,
    CliOutput output
) : Command<InstanceImportCommand.Arguments>
{
    protected override int Execute(
        CommandContext context,
        Arguments settings,
        CancellationToken cancellationToken
    )
    {
        var result = InstanceOperation.ImportAsync(
            profileManager,
            importerAgent,
            settings.Path,
            settings.Name,
            settings.Identity
        ).GetAwaiter().GetResult();

        if (output.UseStructuredOutput)
        {
            output.WriteData(result);
        }
        else
        {
            output.WriteKeyValueTable(
                "Instance imported",
                ("Key", result.Key),
                ("Name", result.Name),
                ("Version", result.Version),
                ("Loader", result.Loader),
                ("Source", result.Path)
            );
            output.WriteSuccess($"Instance {result.Key} imported.");
        }

        return ExitCodes.Success;
    }

    public class Arguments : CommandSettings
    {
        [CommandOption("--identity <KEY>")]
        public string? Identity { get; set; }

        [CommandOption("-n|--name <NAME>")]
        public string? Name { get; set; }

        [CommandArgument(0, "<PATH>")]
        public required string Path { get; set; }
    }
}
