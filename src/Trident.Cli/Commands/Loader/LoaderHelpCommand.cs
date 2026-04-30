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

        AnsiConsole.Write(
            new Panel(
                    new Markup("Loader URLs use the format [cyan]<loader-id>:<loader-version>[/]\nExample: [green]net.neoforged:21.1.200[/]")
                )
                .Header("Loader URL format")
                .RoundedBorder()
                .BorderColor(Color.Blue)
        );
        var table = new Table().RoundedBorder();
        table.Title = new TableTitle("[bold]Supported loaders[/]");
        table.AddColumn("Name");
        table.AddColumn("Loader ID");
        table.AddColumn("Prism UID");
        foreach (var loader in LoaderSupport.Supported)
        {
            table.AddEscapedRow(loader.Name, loader.Identity, loader.PrismUid);
        }

        output.WriteTable(table);
        return ExitCodes.Success;
    }

    public class Arguments : CommandSettings { }
}
