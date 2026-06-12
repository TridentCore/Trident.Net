using Spectre.Console.Cli;
using TridentCore.Cli.Operations;
using TridentCore.Cli.Services;

namespace TridentCore.Cli.Commands.Repository;

public class RepositoryAddCommand(UserRepositoryStore userRepositories, CliOutput output)
    : Command<RepositoryAddCommand.Arguments>
{
    protected override int Execute(
        CommandContext context,
        Arguments settings,
        CancellationToken cancellationToken
    )
    {
        var result = RepositoryOperation.Add(
            userRepositories,
            settings.Label,
            settings.Driver,
            settings.Endpoint,
            settings.ApiKey,
            settings.UserAgent
        );

        if (output.UseStructuredOutput)
        {
            output.WriteData(new
            {
                action = "repository.add",
                result.Label,
                result.Driver,
                result.Endpoint,
                hasAuthorization = result.HasAuthorization,
                result.UserAgent,
            });
        }
        else
        {
            output.WriteKeyValueTable(
                "Repository saved",
                ("Label", result.Label),
                ("Driver", result.Driver),
                ("Endpoint", result.Endpoint),
                ("Authorization", result.HasAuthorization ? "yes" : "no"),
                ("User Agent", result.UserAgent)
            );
            output.WriteSuccess($"Repository {result.Label} saved.");
        }

        return ExitCodes.SUCCESS;
    }

    public class Arguments : CommandSettings
    {
        [CommandOption("--label <LABEL>", true)]
        public required string Label { get; set; }

        [CommandOption("--driver <DRIVER>")]
        public string? Driver { get; set; }

        [CommandOption("--endpoint <URI>", true)]
        public required string Endpoint { get; set; }

        [CommandOption("--api-key <KEY>")]
        public string? ApiKey { get; set; }

        [CommandOption("--user-agent <VALUE>")]
        public string? UserAgent { get; set; }
    }
}
