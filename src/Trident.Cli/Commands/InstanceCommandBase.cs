using Spectre.Console.Cli;
using Trident.Cli.Services;

namespace Trident.Cli.Commands;

public abstract class InstanceCommandBase<T>(InstanceContextResolver resolver) : Command<T>
    where T : InstanceArgumentsBase
{
    protected ResolvedInstanceContext ResolveInstance(T settings) =>
        resolver.Resolve(settings.Instance, settings.Profile);
}
