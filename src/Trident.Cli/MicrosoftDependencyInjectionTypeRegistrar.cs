using Spectre.Console.Cli;
using System;
using System.Collections.Generic;
using Microsoft.Extensions.DependencyInjection;

namespace Trident.Cli;

public class MicrosoftDependencyInjectionTypeRegistrar : ITypeRegistrar
{
    private readonly IServiceCollection _services;
    private readonly IServiceProvider _provider;

    public MicrosoftDependencyInjectionTypeRegistrar(IServiceCollection services)
    {
        _services = services;
    }

    public void Register(Type service, Type implementation)
    {
        _services.AddTransient(service, implementation);
    }

    public void RegisterInstance(Type service, object implementation)
    {
        _services.AddSingleton(service, implementation);
    }

    public void RegisterLazy(Type service, Func<object> factory)
    {
        _services.AddTransient(service, _ => factory());
    }

    public ITypeResolver Build()
    {
        var provider = _services.BuildServiceProvider();
        return new MicrosoftDependencyInjectionTypeResolver(provider);
    }
}

public class MicrosoftDependencyInjectionTypeResolver : ITypeResolver
{
    private readonly IServiceProvider _provider;

    public MicrosoftDependencyInjectionTypeResolver(IServiceProvider provider)
    {
        _provider = provider;
    }

    public object Resolve(Type type)
    {
        return _provider.GetRequiredService(type);
    }
}
