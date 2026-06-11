using TridentCore.Abstractions;
using TridentCore.Abstractions.Accounts;
using TridentCore.Abstractions.FileModels;
using TridentCore.Core.Accounts;
using TridentCore.Core.Igniters;

namespace TridentCore.Core.Services;

public class AccountConfigurerAgent
{
    private readonly Dictionary<Type, IAccountConfigurer> _configurers;

    public AccountConfigurerAgent(IEnumerable<IAccountConfigurer> configurers)
    {
        _configurers = configurers.ToDictionary(c => c.AccountType);
    }

    public IAccountConfigurer Get(IAccount account)
    {
        if (_configurers.TryGetValue(account.GetType(), out var configurer))
            return configurer;

        throw new InvalidOperationException($"No configurer registered for account type {account.GetType().Name}");
    }

    public Task ConfigureLaunchAsync(IAccount account, LaunchContext context, CancellationToken token) =>
        Get(account).ConfigureLaunchAsync(account, context, token);

    public Task<bool> ValidateAsync(IAccount account, CancellationToken token) =>
        Get(account).ValidateAsync(account, token);

    public Task RefreshAsync(IAccount account, CancellationToken token) =>
        Get(account).RefreshAsync(account, token);

    /// <summary>
    ///     Validates the account token and refreshes it if invalid.
    ///     Returns <c>true</c> if a refresh was performed (caller should persist the update).
    /// </summary>
    public async Task<bool> ValidateAndRefreshAsync(IAccount account, CancellationToken token)
    {
        if (await ValidateAsync(account, token).ConfigureAwait(false))
            return false;

        await RefreshAsync(account, token).ConfigureAwait(false);
        return true;
    }

    public class LaunchContext
    {
        public Igniter Igniter { get; }
        public LockData Artifact { get; }

        public LaunchContext(Igniter igniter, LockData artifact)
        {
            Igniter = igniter;
            Artifact = artifact;
        }

        public string GetLibraryPath(LockData.Library library) =>
            PathDef.Default.FileOfLibrary(
                library.Id.Namespace,
                library.Id.Name,
                library.Id.Version,
                library.Id.Platform,
                library.Id.Extension);
    }
}