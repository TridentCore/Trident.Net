using Spectre.Console;
using Spectre.Console.Cli;
using TridentCore.Cli.Services;

namespace TridentCore.Cli.Commands.Account;

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

        if (result.Length == 0)
        {
            output.WriteEmptyState(
                "No accounts",
                "Add one with: trident account add --type offline --username <name>"
            );
            return ExitCodes.Success;
        }

        var table = new Table().RoundedBorder();
        table.Title = new("[bold]Accounts[/]");
        table.AddColumn("UUID");
        table.AddColumn("Username");
        table.AddColumn("Type");
        table.AddColumn("Default");
        table.AddColumn("Enrolled");
        table.AddColumn("Last Used");
        foreach (var account in result)
        {
            var typeColor = string.Equals(
                account.Type,
                "microsoft",
                StringComparison.OrdinalIgnoreCase
            )
                ? "green"
                : "yellow";
            table.AddMarkupRow(
                Markup.Escape(account.Uuid),
                $"[cyan]{Markup.Escape(account.Username)}[/]",
                CliOutput.FormatStatus(account.Type, typeColor),
                CliOutput.FormatBoolean(account.IsDefault, "default", "no"),
                Markup.Escape(account.EnrolledAt.ToString("u")),
                account.LastUsedAt is null
                    ? "[dim]-[/]"
                    : Markup.Escape(account.LastUsedAt.Value.ToString("u"))
            );
        }

        output.WriteTable(table);
        return ExitCodes.Success;
    }

    public class Arguments : CommandSettings { }
}
