using Spectre.Console.Cli;
using TridentCore.Cli.Operations;
using TridentCore.Cli.Services;

namespace TridentCore.Cli.Commands.Repository;

public class RepositoryRemoveCommand(UserRepositoryStore userRepositories, CliOutput output)
    : Command<RepositoryRemoveCommand.Arguments>
{
    protected override int Execute(CommandContext context, Arguments settings, CancellationToken cancellationToken)
    {
        output.RequireConfirmation($"Remove user repository '{settings.Label}'?", settings.Yes);
        var result = RepositoryOperation.Remove(userRepositories, settings.Label);

        if (output.UseStructuredOutput)
        {
            output.WriteData(new { action = "repository.remove", result.Label });
        }
        else
        {
            output.WriteKeyValueTable("Repository removed", ("Label", result.Label));
            output.WriteSuccess($"Repository {result.Label} removed.");
        }

        return ExitCodes.SUCCESS;
    }

    public class Arguments : CommandSettings
    {
        [CommandOption("--label <LABEL>", true)]
        public required string Label { get; set; }

        [CommandOption("-y|--yes")]
        public bool Yes { get; set; }
    }
}
