using Trident.Cli.Services;

namespace Trident.Cli.Commands.Account;

internal static class AccountDtos
{
    public static AccountDto FromStored(StoredAccount account) =>
        new(
            account.Uuid,
            account.Username,
            account.Type,
            account.EnrolledAt,
            account.LastUsedAt,
            account.IsDefault
        );
}

internal sealed record AccountDto(
    string Uuid,
    string Username,
    string Type,
    DateTimeOffset EnrolledAt,
    DateTimeOffset? LastUsedAt,
    bool IsDefault
);
