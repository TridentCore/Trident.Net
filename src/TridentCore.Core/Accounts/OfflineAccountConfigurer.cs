using TridentCore.Abstractions.Accounts;
using TridentCore.Core.Services;

namespace TridentCore.Core.Accounts;

public class OfflineAccountConfigurer : IAccountConfigurer
{
    public Type AccountType => typeof(OfflineAccount);

    public Task ConfigureLaunchAsync(IAccount account, AccountConfigurerAgent.LaunchContext context, CancellationToken token) =>
        Task.CompletedTask;

    public Task<bool> ValidateAsync(IAccount account, CancellationToken token) =>
        Task.FromResult(true);

    public Task RefreshAsync(IAccount account, CancellationToken token) =>
        Task.CompletedTask;
}
