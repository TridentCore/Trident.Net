using Spectre.Console;
using Spectre.Console.Cli;
using Trident.Cli.Services;

namespace Trident.Cli.Commands.Repository;

public class RepositoryHelpCommand(CliOutput output) : Command<RepositoryHelpCommand.Arguments>
{
    protected override int Execute(
        CommandContext context,
        Arguments settings,
        CancellationToken cancellationToken
    )
    {
        var drivers = new[]
        {
            new { driver = "curseforge", apiKeyHeader = "x-api-key" },
            new { driver = "modrinth", apiKeyHeader = "Authorization" },
        };

        if (output.UseStructuredOutput)
        {
            output.WriteData(new { supportedDrivers = drivers });
            return ExitCodes.Success;
        }

        var table = new Table().RoundedBorder();
        table.AddColumn("Driver");
        table.AddColumn("API Key Header");
        foreach (var driver in drivers)
        {
            table.AddEscapedRow(driver.driver, driver.apiKeyHeader);
        }

        output.WriteTable(table);
        return ExitCodes.Success;
    }

    public class Arguments : CommandSettings { }
}
