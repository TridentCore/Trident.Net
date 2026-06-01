using Spectre.Console.Cli;
using TridentCore.Cli.Operations;
using TridentCore.Cli.Services;

namespace TridentCore.Cli.Commands.Loader;

public class LoaderGetCommand(InstanceContextResolver resolver, CliOutput output)
    : InstanceCommandBase<LoaderGetCommand.Arguments>(resolver)
{
    protected override int Execute(
        CommandContext context,
        Arguments settings,
        CancellationToken cancellationToken
    )
    {
        var instance = ResolveInstance(settings);
        var result = LoaderOperation.Get(resolver, instance.Key, settings.Profile);
        var loader = result.Loader;

        if (output.UseStructuredOutput)
        {
            output.WriteData(new { key = result.Key, loader });
            return ExitCodes.Success;
        }

        output.WriteKeyValueTable(
            "Instance loader",
            ("Instance", result.Key),
            ("Loader", loader.Lurl),
            ("Name", loader.Name),
            ("Identity", loader.Identity),
            ("Version", loader.Version),
            ("Supported", loader.Supported ? "yes" : "no")
        );
        if (loader.Lurl is null)
        {
            output.WriteWarning("This instance does not have a loader configured.");
        }

        return ExitCodes.Success;
    }

    public class Arguments : InstanceArgumentsBase { }
}
