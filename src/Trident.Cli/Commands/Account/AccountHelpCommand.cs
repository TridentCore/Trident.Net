using Spectre.Console;
using Spectre.Console.Cli;
using Trident.Cli.Services;

namespace Trident.Cli.Commands.Account;

public class AccountHelpCommand(CliOutput output) : Command<AccountHelpCommand.Arguments>
{
    protected override int Execute(
        CommandContext context,
        Arguments settings,
        CancellationToken cancellationToken
    )
    {
        var types = new[]
        {
            new { type = "microsoft", description = "Microsoft device-code flow account." },
            new { type = "offline", description = "Offline account with generated or provided UUID." },
        };

        if (output.UseStructuredOutput)
        {
            output.WriteData(new { supportedTypes = types });
            return ExitCodes.Success;
        }

        AnsiConsole.Write(
            new Panel("Microsoft accounts use device-code login. Offline accounts are local-only and can use a generated UUID.")
                .Header("Account types")
                .RoundedBorder()
                .BorderColor(Color.Blue)
        );
        var table = new Table().RoundedBorder();
        table.Title = new TableTitle("[bold]Supported account types[/]");
        table.AddColumn("Type");
        table.AddColumn("Description");
        foreach (var type in types)
        {
            table.AddMarkupRow($"[cyan]{Markup.Escape(type.type)}[/]", Markup.Escape(type.description));
        }

        output.WriteTable(table);
        return ExitCodes.Success;
    }

    public class Arguments : CommandSettings { }
}
