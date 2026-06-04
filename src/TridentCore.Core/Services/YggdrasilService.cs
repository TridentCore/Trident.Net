using Refit;
using System.Text;
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
        client.BaseAddress = new(serverUrl);
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
                "No game profile available. The account may not add profile.");

        var skinUrl = await GetSkinUrlAsync(serverUrl, response.SelectedProfile.Id, token)
            .ConfigureAwait(false);

        return new(
            new()
            {
                ServerUrl = serverUrl,
                AccessToken = response.AccessToken,
                ClientToken = response.ClientToken,
                Uuid = response.SelectedProfile.Id,
                Username = response.SelectedProfile.Name,
                SkinUrl = skinUrl?.ToString(),
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
        client.BaseAddress = new(serverUrl);
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

    public async Task<AuthlibAccount> RefreshAsync(
        AuthlibAccount account,
        YggdrasilGameProfile? selectedProfile,
        CancellationToken token = default
    )
    {
        var client = clientFactory.CreateClient();
        client.BaseAddress = new (account.ServerUrl);
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

        var skinUrl = await GetSkinUrlAsync(account.ServerUrl, account.Uuid, token)
            .ConfigureAwait(false);
        account.SkinUrl = skinUrl?.ToString();

        return account;
    }

    public async Task<string> GetMetadataBase64Async(
        string serverUrl,
        CancellationToken token = default
    )
    {
        var client = clientFactory.CreateClient();
        client.BaseAddress = new(serverUrl);
        var json = await client.GetStringAsync("", token).ConfigureAwait(false);
        return Convert.ToBase64String(Encoding.UTF8.GetBytes(json));
    }

    public async Task<Uri?> GetSkinUrlAsync(
        string serverUrl,
        string uuid,
        CancellationToken token = default
    )
    {
        var client = clientFactory.CreateClient();
        client.BaseAddress =new(serverUrl);
        var yggdrasil = RestService.For<IYggdrasilClient>(client, REFIT_SETTINGS);

        var response = await yggdrasil.GetProfileAsync(uuid, token).ConfigureAwait(false);

        var texturesProp = response.Properties?.FirstOrDefault(p => p.Name == "textures");
        if (texturesProp is null)
            return null;

        var texturesJson = Encoding.UTF8.GetString(Convert.FromBase64String(texturesProp.Value));
        var textures = JsonSerializer.Deserialize<YggdrasilTexturesData>(texturesJson);

        return textures?.Textures.TryGetValue("SKIN", out var skin) == true
            ? new Uri(skin.Url, UriKind.RelativeOrAbsolute)
            : null;
    }
}
