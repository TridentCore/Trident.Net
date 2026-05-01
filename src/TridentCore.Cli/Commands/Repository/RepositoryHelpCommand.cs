using Spectre.Console;
using Spectre.Console.Cli;
using TridentCore.Cli.Services;

namespace TridentCore.Cli.Commands.Repository;

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

        AnsiConsole.Write(
            new Panel(
                "User repositories can override built-in labels. API keys are stored locally and never printed by list/status commands."
            )
                .Header("Repository drivers")
                .RoundedBorder()
                .BorderColor(Color.Blue)
        );
        var table = new Table().RoundedBorder();
        table.Title = new TableTitle("[bold]Supported drivers[/]");
        table.AddColumn("Driver");
        table.AddColumn("API Key Header");
        foreach (var driver in drivers)
        {
            table.AddMarkupRow(
                $"[cyan]{Markup.Escape(driver.driver)}[/]",
                Markup.Escape(driver.apiKeyHeader)
            );
        }

        output.WriteTable(table);
        return ExitCodes.Success;
    }

    public class Arguments : CommandSettings { }
}
