using TridentCore.Cli.Commands.Account;
using TridentCore.Cli.Services;

namespace TridentCore.Cli.Operations;

internal static class AccountOperation
{
    public static IReadOnlyList<AccountDto> List(AccountStore accounts) =>
        accounts.Load().Select(AccountDtos.FromStored).ToArray();

    public static AccountDto AddOffline(AccountStore accounts, string username, string? uuid)
    {
        if (string.IsNullOrWhiteSpace(username))
        {
            throw new CliException("--username is required for offline accounts.", ExitCodes.USAGE);
        }

        var stored = AccountStore.CreateOffline(username, uuid);
        accounts.AddOrReplace(stored);
        var saved = accounts.Load().First(x => x.Uuid == stored.Uuid);
        return AccountDtos.FromStored(saved);
    }

    public static AccountRemoveResult Remove(AccountStore accounts, string uuid)
    {
        if (!accounts.Remove(uuid))
        {
            throw new CliException($"Account '{uuid}' was not found.", ExitCodes.NOT_FOUND);
        }

        return new(uuid);
    }
}

internal sealed record AccountRemoveResult(string Uuid);
