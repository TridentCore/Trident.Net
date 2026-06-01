using TridentCore.Cli.Commands.Account;
using TridentCore.Cli.Services;

namespace TridentCore.Cli.Operations;

internal static class AccountOperation
{
    public static IReadOnlyList<AccountDto> List(AccountStore accounts) =>
        accounts.Load().Select(AccountDtos.FromStored).ToArray();
}
