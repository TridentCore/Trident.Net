using Refit;
using System.Text.Json;
using TridentCore.Core.Accounts;
using TridentCore.Core.Clients;
using TridentCore.Core.Models.YggdrasilApi;

namespace TridentCore.Core.Services;

public class YggdrasilService(IHttpClientFactory clientFactory)
{
    private static readonly RefitSettings REFIT_SETTINGS =
        new(new SystemTextJsonContentSerializer(new(JsonSerializerDefaults.Web)));

    public async Task<AuthlibInjectorAuthenticationResult> AuthenticateAsync(
        string serverUrl,
        string username,
        string password,
        CancellationToken token = default
    )
    {
        var client = clientFactory.CreateClient();
        client.BaseAddress = NormalizeServerUrl(serverUrl);
        var yggdrasil = RestService.For<IYggdrasilClient>(client, REFIT_SETTINGS);

        var clientToken = Guid.NewGuid().ToString("N");
        var request = new YggdrasilAuthenticateRequest(
            new("Minecraft", 1),
            username,
            password,
            clientToken,
            true
        );

        var response = await yggdrasil.AuthenticateAsync(request, token).ConfigureAwait(false);

        if (response.SelectedProfile is null)
            throw new InvalidOperationException(
                "No game profile available. The account may not own Minecraft.");

        return new(
            new()
            {
                ServerUrl = serverUrl,
                AccessToken = response.AccessToken,
                ClientToken = response.ClientToken,
                Uuid = response.SelectedProfile.Id,
                Username = response.SelectedProfile.Name,
            },
            response.AvailableProfiles,
            serverUrl,
            response.AccessToken,
            response.ClientToken
        );
    }

    public async Task<bool> ValidateAsync(
        string serverUrl,
        string accessToken,
        string? clientToken,
        CancellationToken token = default
    )
    {
        var client = clientFactory.CreateClient();
        client.BaseAddress = NormalizeServerUrl(serverUrl);
        var yggdrasil = RestService.For<IYggdrasilClient>(client, REFIT_SETTINGS);

        try
        {
            await yggdrasil.ValidateAsync(new(accessToken, clientToken), token).ConfigureAwait(false);
            return true;
        }
        catch
        {
            return false;
        }
    }

    public async Task<AuthlibInjectorAccount> RefreshAsync(
        AuthlibInjectorAccount account,
        YggdrasilGameProfile? selectedProfile,
        CancellationToken token = default
    )
    {
        var client = clientFactory.CreateClient();
        client.BaseAddress = NormalizeServerUrl(account.ServerUrl);
        var yggdrasil = RestService.For<IYggdrasilClient>(client, REFIT_SETTINGS);

        var request = new YggdrasilRefreshRequest(
            account.AccessToken,
            account.ClientToken ?? throw new InvalidOperationException("ClientToken is missing"),
            true,
            selectedProfile
        );

        var response = await yggdrasil.RefreshAsync(request, token).ConfigureAwait(false);

        account.AccessToken = response.AccessToken;
        account.ClientToken = response.ClientToken;

        if (response.SelectedProfile is not null)
        {
            account.Uuid = response.SelectedProfile.Id;
            account.Username = response.SelectedProfile.Name;
        }

        return account;
    }

    private static Uri NormalizeServerUrl(string serverUrl)
    {
        if (!serverUrl.StartsWith("http://", StringComparison.OrdinalIgnoreCase)
            && !serverUrl.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
            serverUrl = "https://" + serverUrl;

        return new(serverUrl.TrimEnd('/'));
    }
}
