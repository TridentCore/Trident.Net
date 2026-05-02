using Spectre.Console;
using Spectre.Console.Cli;
using TridentCore.Cli.Services;

namespace TridentCore.Cli.Commands.Loader;

public class LoaderListCommand(CliOutput output) : Command<LoaderListCommand.Arguments>
{
    protected override int Execute(
        CommandContext context,
        Arguments settings,
        CancellationToken cancellationToken
    )
    {
        if (output.UseStructuredOutput)
        {
            output.WriteData(LoaderSupport.Supported);
            return ExitCodes.Success;
        }

        var table = new Table().RoundedBorder();
        table.Title = new("[bold]Supported loaders[/]");
        table.AddColumn("Name");
        table.AddColumn("Loader ID");
        table.AddColumn("Prism UID");
        foreach (var loader in LoaderSupport.Supported)
        {
            table.AddMarkupRow(
                $"[cyan]{Markup.Escape(loader.Name)}[/]",
                Markup.Escape(loader.Identity),
                Markup.Escape(loader.PrismUid)
            );
        }

        output.WriteTable(table);
        return ExitCodes.Success;
    }

    public class Arguments : CommandSettings { }
}
