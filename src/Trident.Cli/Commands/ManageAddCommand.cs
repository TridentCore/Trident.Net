using System.Net.Http;
using Microsoft.Extensions.Logging;
using Spectre.Console;
using Spectre.Console.Cli;

namespace Trident.Cli.Commands;

public class ManageAddCommand : Command<ManageAddCommand.Settings>
{
    public class Settings : ManageBranch.Settings
    {
        [CommandArgument(0, "<name>")]
        public string Name { get; set; }
    }

    private readonly ILogger<ManageAddCommand> _logger;
    private readonly IHttpClientFactory _httpClientFactory;

    public ManageAddCommand(ILogger<ManageAddCommand> logger, IHttpClientFactory httpClientFactory)
    {
        _logger = logger;
        _httpClientFactory = httpClientFactory;
    }

    public override int Execute(CommandContext context, Settings settings)
    {
        _logger.LogInformation("Executing manage add command with name: {Name}", settings.Name);

        AnsiConsole.MarkupLine($"[green]Adding item: {settings.Name}[/]");

        // Simulate adding item
        AnsiConsole.WriteLine($"Item '{settings.Name}' added successfully.");

        return 0;
    }
}
