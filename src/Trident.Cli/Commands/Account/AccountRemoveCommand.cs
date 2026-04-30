using Spectre.Console.Cli;
using Trident.Cli.Services;

namespace Trident.Cli.Commands.Account;

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

        if (!accounts.Remove(settings.Uuid))
        {
            throw new CliException($"Account '{settings.Uuid}' was not found.", ExitCodes.NotFound);
        }

        var result = new { action = "account.remove", uuid = settings.Uuid };
        if (output.UseStructuredOutput)
        {
            output.WriteData(result);
        }
        else
        {
            output.WriteKeyValueTable("Account removed", ("UUID", settings.Uuid));
            output.WriteSuccess($"Account {settings.Uuid} removed.");
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
