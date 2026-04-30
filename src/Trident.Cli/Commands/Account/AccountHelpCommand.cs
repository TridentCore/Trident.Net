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

        var table = new Table().RoundedBorder();
        table.AddColumn("Type");
        table.AddColumn("Description");
        foreach (var type in types)
        {
            table.AddEscapedRow(type.type, type.description);
        }

        output.WriteTable(table);
        return ExitCodes.Success;
    }

    public class Arguments : CommandSettings { }
}
