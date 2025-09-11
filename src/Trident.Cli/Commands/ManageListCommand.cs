using System.Net.Http;
using Microsoft.Extensions.Logging;
using Spectre.Console;
using Spectre.Console.Cli;

namespace Trident.Cli.Commands;

public class ManageListCommand : Command<ManageListCommand.Settings>
{
    public class Settings : ManageBranch.Settings
    {
    }

    private readonly ILogger<ManageListCommand> _logger;
    private readonly IHttpClientFactory _httpClientFactory;

    public ManageListCommand(ILogger<ManageListCommand> logger, IHttpClientFactory httpClientFactory)
    {
        _logger = logger;
        _httpClientFactory = httpClientFactory;
    }

    public override int Execute(CommandContext context, Settings settings)
    {
        _logger.LogInformation("Executing manage list command.");

        AnsiConsole.MarkupLine("[green]Listing managed items...[/]");
        AnsiConsole.WriteLine("- Item 1");
        AnsiConsole.WriteLine("- Item 2");

        return 0;
    }
}
