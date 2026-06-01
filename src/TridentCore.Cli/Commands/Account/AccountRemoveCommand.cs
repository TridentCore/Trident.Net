using Spectre.Console.Cli;
using TridentCore.Cli.Operations;
using TridentCore.Cli.Services;

namespace TridentCore.Cli.Commands.Account;

public class AccountRemoveCommand(AccountStore accounts, CliOutput output)
    : Command<AccountRemoveCommand.Arguments>
{
    protected override int Execute(
        CommandContext context,
        Arguments settings,
        CancellationToken cancellationToken
    )
    {
        output.RequireConfirmation($"Remove account '{settings.Uuid}'?", settings.Yes);
        var result = AccountOperation.Remove(accounts, settings.Uuid);

        if (output.UseStructuredOutput)
        {
            output.WriteData(new { action = "account.remove", uuid = result.Uuid });
        }
        else
        {
            output.WriteKeyValueTable("Account removed", ("UUID", result.Uuid));
            output.WriteSuccess($"Account {result.Uuid} removed.");
        }

        return ExitCodes.Success;
    }

    public class Arguments : CommandSettings
    {
        [CommandOption("--uuid <UUID>", true)]
        public required string Uuid { get; set; }

        [CommandOption("-y|--yes")]
        public bool Yes { get; set; }
    }
}
