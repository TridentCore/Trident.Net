using Microsoft.Extensions.DependencyInjection;
using Spectre.Console.Cli;

namespace TridentCore.Cli;

public class TypeResolver : ITypeResolver, IDisposable
{
    private readonly IServiceProvider _provider;

    public TypeResolver(IServiceProvider provider) => _provider = provider;

    #region ITypeResolver Members

    public object Resolve(Type? type)
    {
        ArgumentNullException.ThrowIfNull(type);
        return _provider.GetRequiredService(type);
    }

    public void Dispose()
    {
        if (_provider is IDisposable disposable)
        {
            disposable.Dispose();
        }
    }

    #endregion
}
