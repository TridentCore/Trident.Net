using Microsoft.Extensions.DependencyInjection;
using Spectre.Console.Cli;

namespace Trident.Cli;

public class TypeRegistrar(IServiceCollection services) : ITypeRegistrar
{
    #region ITypeRegistrar Members

    public void Register(Type service, Type implementation) => services.AddTransient(service, implementation);

    public void RegisterInstance(Type service, object implementation) => services.AddSingleton(service, implementation);

    public void RegisterLazy(Type service, Func<object> factory) => services.AddTransient(service, _ => factory());

    public ITypeResolver Build()
    {
        var provider = services.BuildServiceProvider();
        return new TypeResolver(provider);
    }

    #endregion
}
