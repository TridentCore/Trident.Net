using Spectre.Console;
using Spectre.Console.Cli;
using Trident.Cli.Services;

namespace Trident.Cli.Commands.Account;

public class AccountListCommand(AccountStore accounts, CliOutput output)
    : Command<AccountListCommand.Arguments>
{
    protected override int Execute(
        CommandContext context,
        Arguments settings,
        CancellationToken cancellationToken
    )
    {
        var result = accounts.Load().Select(AccountDtos.FromStored).ToArray();
        if (output.UseStructuredOutput)
        {
            output.WriteData(result);
            return ExitCodes.Success;
        }

        var table = new Table().RoundedBorder();
        table.AddColumn("UUID");
        table.AddColumn("Username");
        table.AddColumn("Type");
        table.AddColumn("Default");
        foreach (var account in result)
        {
            table.AddEscapedRow(account.Uuid, account.Username, account.Type, account.IsDefault.ToString());
        }

        output.WriteTable(table);
        return ExitCodes.Success;
    }

    public class Arguments : CommandSettings { }
}
