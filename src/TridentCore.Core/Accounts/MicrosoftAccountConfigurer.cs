using System.Net;
using Refit;
using TridentCore.Abstractions.Accounts;
using TridentCore.Core.Services;

namespace TridentCore.Core.Accounts;

public class MicrosoftAccountConfigurer : IAccountConfigurer
{
    private readonly MicrosoftService _microsoft;
    private readonly XboxLiveService _xbox;
    private readonly MinecraftService _minecraft;

    public MicrosoftAccountConfigurer(MicrosoftService microsoft, XboxLiveService xbox, MinecraftService minecraft)
    {
        _microsoft = microsoft;
        _xbox = xbox;
        _minecraft = minecraft;
    }

    public Type AccountType => typeof(MicrosoftAccount);

    public Task ConfigureLaunchAsync(IAccount account, AccountConfigurerAgent.LaunchContext context, CancellationToken token) =>
        Task.CompletedTask;

    public async Task<bool> ValidateAsync(IAccount account, CancellationToken token)
    {
        var msa = (MicrosoftAccount)account;

        var shouldValidate = msa.AccessTokenExpiresAt is null
                         || DateTimeOffset.UtcNow >= msa.AccessTokenExpiresAt.Value.AddMinutes(-5);

        if (!shouldValidate)
            return true;

        try
        {
            await _minecraft.AcquireAccountProfileByMinecraftTokenAsync(msa.AccessToken).ConfigureAwait(false);
            return true;
        }
        catch (ApiException ex) when (ex.StatusCode == HttpStatusCode.Unauthorized)
        {
            return false;
        }
    }

    public async Task RefreshAsync(IAccount account, CancellationToken token)
    {
        var msa = (MicrosoftAccount)account;

        var microsoft = await _microsoft.RefreshUserAsync(msa.RefreshToken).ConfigureAwait(false);
        var xbox = await _xbox.AuthenticateForXboxLiveTokenByMicrosoftTokenAsync(microsoft.AccessToken).ConfigureAwait(false);
        var xsts = await _xbox.AuthorizeForServiceTokenByXboxLiveTokenAsync(xbox.Token).ConfigureAwait(false);
        var minecraft = await _minecraft.AuthenticateByXboxLiveServiceTokenAsync(xsts.Token, xsts.DisplayClaims.Xui.First().Uhs).ConfigureAwait(false);

        msa.AccessToken = minecraft.AccessToken;
        msa.AccessTokenExpiresAt = DateTimeOffset.UtcNow.AddSeconds(minecraft.ExpiresIn);
        msa.RefreshToken = !string.IsNullOrEmpty(microsoft.RefreshToken)
                               ? microsoft.RefreshToken
                               : msa.RefreshToken;
    }
}
