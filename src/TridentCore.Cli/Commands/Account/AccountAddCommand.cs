using System.Linq;
using Spectre.Console;
using Spectre.Console.Cli;
using TridentCore.Cli.Services;
using TridentCore.Core.Accounts;
using TridentCore.Core.Services;

namespace TridentCore.Cli.Commands.Account;

public class AccountAddCommand(
    AccountStore accounts,
    MicrosoftService microsoftService,
    XboxLiveService xboxLiveService,
    MinecraftService minecraftService,
    CliOutput output
) : Command<AccountAddCommand.Arguments>
{
    protected override int Execute(
        CommandContext context,
        Arguments settings,
        CancellationToken cancellationToken
    )
    {
        ExecuteAsync(settings, cancellationToken).GetAwaiter().GetResult();
        return ExitCodes.Success;
    }

    private async Task ExecuteAsync(Arguments settings, CancellationToken cancellationToken)
    {
        var stored = settings.Type.ToLowerInvariant() switch
        {
            "offline" => AddOffline(settings),
            "microsoft" => await AddMicrosoftAsync(cancellationToken).ConfigureAwait(false),
            _ => throw new CliException(
                $"Account type '{settings.Type}' is not supported.",
                ExitCodes.Usage
            ),
        };

        accounts.AddOrReplace(stored);
        var saved = accounts.Load().First(x => x.Uuid == stored.Uuid);
        var result = new { action = "account.add", account = AccountDtos.FromStored(saved) };
        if (output.UseStructuredOutput)
        {
            output.WriteData(result);
        }
        else
        {
            output.WriteKeyValueTable(
                "Account added",
                ("Username", saved.Username),
                ("UUID", saved.Uuid),
                ("Type", saved.Type),
                ("Default", saved.IsDefault ? "yes" : "no")
            );
            output.WriteSuccess($"Account {stored.Username} added.");
        }
    }

    private static StoredAccount AddOffline(Arguments settings)
    {
        if (string.IsNullOrWhiteSpace(settings.Username))
        {
            throw new CliException("--username is required for offline accounts.", ExitCodes.Usage);
        }

        return AccountStore.CreateOffline(settings.Username, settings.Uuid);
    }

    private async Task<StoredAccount> AddMicrosoftAsync(CancellationToken cancellationToken)
    {
        var code = await output
            .StatusAsync(
                "Requesting Microsoft device code...",
                async () => await microsoftService.AcquireUserCodeAsync().ConfigureAwait(false)
            )
            .ConfigureAwait(false);
        var verificationUri = code.VerificationUri ?? new Uri("https://aka.ms/devicelogin");
        WriteDeviceCode(code.UserCode, verificationUri, code.ExpiresIn);

        var microsoft = await output
            .StatusAsync(
                "Waiting for Microsoft authorization...",
                async () =>
                    await microsoftService
                        .AuthenticateAsync(code.DeviceCode, code.Interval, cancellationToken)
                        .ConfigureAwait(false)
            )
            .ConfigureAwait(false);
        var xbox = await output
            .StatusAsync(
                "Authenticating with Xbox Live...",
                async () =>
                    await xboxLiveService
                        .AuthenticateForXboxLiveTokenByMicrosoftTokenAsync(microsoft.AccessToken)
                        .ConfigureAwait(false)
            )
            .ConfigureAwait(false);
        var xsts = await output
            .StatusAsync(
                "Authorizing Xbox service token...",
                async () =>
                    await xboxLiveService
                        .AuthorizeForServiceTokenByXboxLiveTokenAsync(xbox.Token)
                        .ConfigureAwait(false)
            )
            .ConfigureAwait(false);
        var minecraft = await output
            .StatusAsync(
                "Authenticating with Minecraft services...",
                async () =>
                    await minecraftService
                        .AuthenticateByXboxLiveServiceTokenAsync(
                            xsts.Token,
                            xsts.DisplayClaims.Xui.First().Uhs
                        )
                        .ConfigureAwait(false)
            )
            .ConfigureAwait(false);
        var profile = await output
            .StatusAsync(
                "Loading Minecraft profile...",
                async () =>
                    await minecraftService
                        .AcquireAccountProfileByMinecraftTokenAsync(minecraft.AccessToken)
                        .ConfigureAwait(false)
            )
            .ConfigureAwait(false);

        return AccountStore.CreateMicrosoft(
            new MicrosoftAccount
            {
                AccessToken = minecraft.AccessToken,
                AccessTokenExpiresAt = DateTimeOffset.UtcNow.AddSeconds(minecraft.ExpiresIn),
                RefreshToken = microsoft.RefreshToken,
                Uuid = profile.Id,
                Username = profile.Name,
            }
        );
    }

    private void WriteDeviceCode(string userCode, Uri verificationUri, int expiresIn)
    {
        if (output.UseStructuredOutput)
        {
            Console.Error.WriteLine(
                $"Open {verificationUri} and enter code {userCode}. Code expires in {expiresIn} seconds."
            );
            return;
        }

        AnsiConsole.Write(
            new Panel(
                new Markup(
                    $"Open [blue]{Markup.Escape(verificationUri.ToString())}[/]\nEnter code [green]{Markup.Escape(userCode)}[/]\n[dim]Code expires in {expiresIn} seconds.[/]"
                )
            )
                .Header("Microsoft device login")
                .RoundedBorder()
                .BorderColor(Color.Blue)
        );
    }

    public class Arguments : CommandSettings
    {
        [CommandOption("--type <TYPE>", true)]
        public required string Type { get; set; }

        [CommandOption("--username <USERNAME>")]
        public string? Username { get; set; }

        [CommandOption("--uuid <UUID>")]
        public string? Uuid { get; set; }
    }
}
