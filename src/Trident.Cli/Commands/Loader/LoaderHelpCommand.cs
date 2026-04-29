using Spectre.Console;
using Spectre.Console.Cli;
using Trident.Cli.Services;

namespace Trident.Cli.Commands.Loader;

public class LoaderHelpCommand(CliOutput output) : Command<LoaderHelpCommand.Arguments>
{
    protected override int Execute(
        CommandContext context,
        Arguments settings,
        CancellationToken cancellationToken
    )
    {
        var result = new
        {
            lurl = "<loader-id>:<loader-version>",
            example = "net.neoforged:21.1.200",
            supported = LoaderSupport.Supported,
        };

        if (output.UseStructuredOutput)
        {
            output.WriteData(result);
            return ExitCodes.Success;
        }

        AnsiConsole.WriteLine("Loader URLs use the format <loader-id>:<loader-version>.");
        AnsiConsole.WriteLine("Example: net.neoforged:21.1.200");
        var table = new Table().RoundedBorder();
        table.AddColumn("Name");
        table.AddColumn("Loader ID");
        table.AddColumn("Prism UID");
        foreach (var loader in LoaderSupport.Supported)
        {
            table.AddRow(loader.Name, loader.Identity, loader.PrismUid);
        }

        output.WriteTable(table);
        return ExitCodes.Success;
    }

    public class Arguments : CommandSettings { }
}
