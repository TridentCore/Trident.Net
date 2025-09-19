using Microsoft.Extensions.DependencyInjection;
using Spectre.Console.Cli;

namespace Trident.Cli;

public class TypeResolver : ITypeResolver
{
    private readonly IServiceProvider _provider;

    public TypeResolver(IServiceProvider provider) => _provider = provider;

    #region ITypeResolver Members

    public object Resolve(Type? type)
    {
        ArgumentNullException.ThrowIfNull(type);
        return _provider.GetRequiredService(type);
    }

    #endregion
}
