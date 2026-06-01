using Spectre.Console;
using Spectre.Console.Cli;
using TridentCore.Cli.Operations;
using TridentCore.Cli.Services;

namespace TridentCore.Cli.Commands.Repository;

public class RepositoryListCommand(
    UserRepositoryStore userRepositories,
    CliRepositoryProviderAccessor combined,
    CliOutput output
) : Command<RepositoryListCommand.Arguments>
{
    protected override int Execute(
        CommandContext context,
        Arguments settings,
        CancellationToken cancellationToken
    )
    {
        var repositories = RepositoryOperation.List(userRepositories, combined);

        if (output.UseStructuredOutput)
        {
            output.WriteData(repositories);
            return ExitCodes.Success;
        }

        if (repositories.Count == 0)
        {
            output.WriteEmptyState(
                "No repositories",
                "Add one with: trident repository add --label <label> --endpoint <uri>"
            );
            return ExitCodes.Success;
        }

        var table = new Table().RoundedBorder();
        table.Title = new("[bold]Repositories[/]");
        table.AddColumn("Label");
        table.AddColumn("Driver");
        table.AddColumn("Endpoint");
        table.AddColumn("User");
        table.AddColumn("Auth");
        foreach (var repo in repositories)
        {
            table.AddMarkupRow(
                $"[cyan]{Markup.Escape(repo.Label)}[/]",
                Markup.Escape(repo.Driver),
                Markup.Escape(repo.Endpoint),
                CliOutput.FormatBoolean(repo.UserDefined, "user", "built-in"),
                CliOutput.FormatBoolean(repo.HasAuthorization)
            );
        }

        output.WriteTable(table);
        return ExitCodes.Success;
    }

    public class Arguments : CommandSettings { }
}
