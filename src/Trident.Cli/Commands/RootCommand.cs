using Microsoft.Extensions.Logging;
using Spectre.Console;
using Spectre.Console.Cli;

namespace Trident.Cli.Commands;

public class RootCommand : Command<RootCommand.Settings>
{
    public class Settings : CommandSettings
    {
    }

    private readonly ILogger<RootCommand> _logger;

    public RootCommand(ILogger<RootCommand> logger)
    {
        _logger = logger;
    }

    public override int Execute(CommandContext context, Settings settings)
    {
        _logger.LogInformation("Root command executed.");
        AnsiConsole.WriteLine("Welcome to Trident CLI!");
        return 0;
    }
}
