using Spectre.Console;
using Spectre.Console.Cli;
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
        var userLabels = userRepositories.Load()
            .Select(x => x.Label)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var repositories = combined
            .Build()
            .Select(x => RepositoryDtos.FromProvider(x, userLabels.Contains(x.Label)))
            .ToArray();

        if (output.UseStructuredOutput)
        {
            output.WriteData(repositories);
            return ExitCodes.Success;
        }

        if (repositories.Length == 0)
        {
            output.WriteEmptyState("No repositories", "Add one with: trident repository add --label <label> --endpoint <uri>");
            return ExitCodes.Success;
        }

        var table = new Table().RoundedBorder();
        table.Title = new TableTitle("[bold]Repositories[/]");
        table.AddColumn("Label");
        table.AddColumn("Driver");
        table.AddColumn("Endpoint");
        table.AddColumn("User");
        table.AddColumn("Auth");
        foreach (var repository in repositories)
        {
            table.AddMarkupRow(
                $"[cyan]{Markup.Escape(repository.Label)}[/]",
                Markup.Escape(repository.Driver),
                Markup.Escape(repository.Endpoint),
                CliOutput.FormatBoolean(repository.UserDefined, "user", "built-in"),
                CliOutput.FormatBoolean(repository.HasAuthorization)
            );
        }

        output.WriteTable(table);
        return ExitCodes.Success;
    }

    public class Arguments : CommandSettings { }
}
