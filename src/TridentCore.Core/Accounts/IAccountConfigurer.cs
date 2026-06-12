using TridentCore.Abstractions.Accounts;
using TridentCore.Core.Services;

namespace TridentCore.Core.Accounts;

public interface IAccountConfigurer
{
    Type AccountType { get; }

    Task ConfigureLaunchAsync(IAccount account, AccountConfigurerAgent.LaunchContext context, CancellationToken token);
    Task<bool> ValidateAsync(IAccount account, CancellationToken token);
    Task RefreshAsync(IAccount account, CancellationToken token);
}
