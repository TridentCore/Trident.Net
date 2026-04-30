using Spectre.Console;
using Spectre.Console.Cli;
using Trident.Cli.Services;

namespace Trident.Cli.Commands.Repository;

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

        var table = new Table().RoundedBorder();
        table.AddColumn("Label");
        table.AddColumn("Driver");
        table.AddColumn("Endpoint");
        table.AddColumn("User");
        table.AddColumn("Auth");
        foreach (var repository in repositories)
        {
            table.AddEscapedRow(
                repository.Label,
                repository.Driver,
                repository.Endpoint,
                repository.UserDefined.ToString(),
                repository.HasAuthorization ? "yes" : "no"
            );
        }

        output.WriteTable(table);
        return ExitCodes.Success;
    }

    public class Arguments : CommandSettings { }
}
