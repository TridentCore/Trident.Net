using Spectre.Console.Cli;
using TridentCore.Cli.Services;

namespace TridentCore.Cli.Commands;

public abstract class InstanceCommandBase<T>(InstanceContextResolver resolver) : Command<T>
    where T : InstanceArgumentsBase
{
    protected InstanceContextResolver Resolver { get; } = resolver;

    protected ResolvedInstanceContext ResolveInstance(T settings) =>
        Resolver.Resolve(settings.Instance, settings.Profile);
}
