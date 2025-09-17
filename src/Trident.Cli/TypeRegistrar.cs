using Spectre.Console.Cli;
using System;
using System.Collections.Generic;
using Microsoft.Extensions.DependencyInjection;

namespace Trident.Cli;

public class TypeRegistrar(IServiceCollection services) : ITypeRegistrar
{
    public void Register(Type service, Type implementation)
    {
        services.AddTransient(service, implementation);
    }

    public void RegisterInstance(Type service, object implementation)
    {
        services.AddSingleton(service, implementation);
    }

    public void RegisterLazy(Type service, Func<object> factory)
    {
        services.AddTransient(service, _ => factory());
    }

    public ITypeResolver Build()
    {
        var provider = services.BuildServiceProvider();
        return new TypeResolver(provider);
    }
}
