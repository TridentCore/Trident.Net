using Spectre.Console.Cli;
using Trident.Cli.Services;

namespace Trident.Cli.Commands.Repository;

public class RepositoryRemoveCommand(UserRepositoryStore userRepositories, CliOutput output)
    : Command<RepositoryRemoveCommand.Arguments>
{
    protected override int Execute(
        CommandContext context,
        Arguments settings,
        CancellationToken cancellationToken
    )
    {
        output.RequireConfirmation($"Remove user repository '{settings.Label}'?", settings.Yes);

        if (!userRepositories.Remove(settings.Label))
        {
            throw new CliException(
                $"User repository '{settings.Label}' was not found.",
                ExitCodes.NotFound
            );
        }

        var result = new { action = "repository.remove", settings.Label };
        if (output.UseStructuredOutput)
        {
            output.WriteData(result);
        }
        else
        {
            output.WriteMessage($"Repository {settings.Label} removed.");
        }

        return ExitCodes.Success;
    }

    public class Arguments : CommandSettings
    {
        [CommandOption("--label <LABEL>", true)]
        public required string Label { get; set; }

        [CommandOption("-y|--yes")]
        public bool Yes { get; set; }
    }
}
