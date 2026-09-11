using Refit;
using TridentCore.Abstractions.Accounts;
using TridentCore.Core.Exceptions;
using TridentCore.Core.Services;

namespace TridentCore.Core.Accounts;

public class AuthlibAccountConfigurer : IAccountConfigurer
{
    private readonly YggdrasilService _yggdrasil;

    public AuthlibAccountConfigurer(YggdrasilService yggdrasil) => _yggdrasil = yggdrasil;

    public Type AccountType => typeof(AuthlibAccount);

    public async Task ConfigureLaunchAsync(
        IAccount account,
        AccountConfigurerAgent.LaunchContext context,
        CancellationToken token)
    {
        var ai = (AuthlibAccount)account;

        var library = context.Lock.Artifact!.Libraries.LastOrDefault(x => x is
        {
            IsNative: false,
            IsPresent: false,
            Id:
            {
                Namespace: AuthlibInjectorService.LIBRARY_NAMESPACE,
                Name: AuthlibInjectorService.LIBRARY_NAME,
                Platform: null,
                Extension: "jar"
            }
        });

        if (library is null)
        {
            throw new AccountConfigurationException($"Authlib-injector library not found in artifact for account {ai.Username}. "
                                                    + "The deployment may be incomplete.");
        }

        context.Igniter.AddJavaAgent(new(library.Id, context.GetLibraryPath(library), ai.ServerUrl));

        var prefetched = await _yggdrasil.GetMetadataBase64Async(ai.ServerUrl, token).ConfigureAwait(false);
        context.Igniter.AddJvmArgument($"-Dauthlibinjector.yggdrasil.prefetched={prefetched}");
    }

    public async Task<bool> ValidateAsync(IAccount account, CancellationToken token)
    {
        var ai = (AuthlibAccount)account;
        return await _yggdrasil
                    .ValidateAsync(ai.ServerUrl, ai.AccessToken, ai.ClientToken, token)
                    .ConfigureAwait(false);
    }

    public async Task RefreshAsync(IAccount account, CancellationToken token)
    {
        var ai = (AuthlibAccount)account;
        try
        {
            var response = await _yggdrasil
                                .RefreshAsync(ai.ServerUrl,
                                              ai.AccessToken,
                                              ai.ClientToken!,
                                              new(ai.Uuid, ai.Username),
                                              token)
                                .ConfigureAwait(false);
            ai.AccessToken = response.AccessToken;
            ai.ClientToken = response.ClientToken;
        }
        catch (ApiException ex)
        {
            throw new
                AccountAuthenticationException("Unable to refresh the expired authlib-injector session. Please re-authenticate.",
                                               ex);
        }
    }
}
