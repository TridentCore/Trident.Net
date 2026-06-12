using Spectre.Console;
using Spectre.Console.Cli;
using TridentCore.Cli.Operations;
using TridentCore.Cli.Services;
using TridentCore.Core.Services;

namespace TridentCore.Cli.Commands.Repository;

public class RepositoryStatusCommand(RepositoryAgent repositories, CliOutput output)
    : Command<RepositoryStatusCommand.Arguments>
{
    protected override int Execute(
        CommandContext context,
        Arguments settings,
        CancellationToken cancellationToken
    )
    {
        ExecuteAsync(settings, cancellationToken).GetAwaiter().GetResult();
        return ExitCodes.SUCCESS;
    }

    private async Task ExecuteAsync(Arguments settings, CancellationToken cancellationToken)
    {
        var results = await RepositoryOperation.Status(repositories, settings.Label).ConfigureAwait(false);

        if (output.UseStructuredOutput)
        {
            output.WriteData(results);
            return;
        }

        if (results.Count == 0)
        {
            output.WriteEmptyState("No repositories", "No repository providers are configured.");
            return;
        }

        var table = new Table().RoundedBorder();
        table.Title = new("[bold]Repository status[/]");
        table.AddColumn("Label");
        table.AddColumn("Loaders");
        table.AddColumn("Versions");
        table.AddColumn("Kinds");
        foreach (var result in results)
        {
            table.AddEscapedRow(
                result.Label,
                string.Join(",", result.SupportedLoaders),
                result.VersionCount.ToString(),
                string.Join(",", result.SupportedKinds)
            );
        }

        output.WriteTable(table);
    }

    public class Arguments : CommandSettings
    {
        [CommandOption("--label <LABEL>")]
        public string? Label { get; set; }
    }
}
