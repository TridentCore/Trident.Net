using Spectre.Console.Cli;
using Trident.Cli.Services;
using Trident.Core.Services;

namespace Trident.Cli.Commands.Repository;

public class RepositoryAddCommand(UserRepositoryStore userRepositories, CliOutput output)
    : Command<RepositoryAddCommand.Arguments>
{
    protected override int Execute(
        CommandContext context,
        Arguments settings,
        CancellationToken cancellationToken
    )
    {
        var driver = settings.Driver ?? settings.Label;
        UserRepositoryStore.ParseDriver(driver);
        if (!Uri.TryCreate(settings.Endpoint, UriKind.Absolute, out _))
        {
            throw new CliException("--endpoint must be an absolute URI.", ExitCodes.Usage);
        }

        var repository = new UserRepositoryProfile(
            settings.Label,
            driver,
            settings.Endpoint,
            settings.ApiKey,
            settings.UserAgent
        );
        userRepositories.AddOrReplace(repository);

        var result = new
        {
            action = "repository.add",
            repository.Label,
            repository.Driver,
            repository.Endpoint,
            hasAuthorization = !string.IsNullOrWhiteSpace(repository.ApiKey),
            repository.UserAgent,
        };

        if (output.UseStructuredOutput)
        {
            output.WriteData(result);
        }
        else
        {
            output.WriteMessage($"Repository {repository.Label} saved.");
        }

        return ExitCodes.Success;
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
