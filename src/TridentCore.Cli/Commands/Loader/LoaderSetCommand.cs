using Spectre.Console.Cli;
using TridentCore.Abstractions.Utilities;
using TridentCore.Cli.Services;
using TridentCore.Core.Services;

namespace TridentCore.Cli.Commands.Loader;

public class LoaderSetCommand(
    InstanceContextResolver resolver,
    ProfileManager profileManager,
    CliOutput output
) : InstanceCommandBase<LoaderSetCommand.Arguments>(resolver)
{
    protected override int Execute(
        CommandContext context,
        Arguments settings,
        CancellationToken cancellationToken
    )
    {
        if (!LoaderHelper.TryParse(settings.Loader, out var parsed))
        {
            throw new CliException(
                $"Loader '{settings.Loader}' is not a valid lurl. Use <loader-id>:<version>.",
                ExitCodes.Usage
            );
        }

        if (!LoaderSupport.IsSupported(parsed.Identity))
        {
            throw new CliException(
                $"Loader '{parsed.Identity}' is not supported.",
                ExitCodes.Usage
            );
        }

        var instance = ResolveInstance(settings);
        var guard = profileManager.GetMutable(instance.Key);
        var oldLoader = guard.Value.Setup.Loader;
        guard.Value.Setup.Loader = settings.Loader;
        guard.DisposeAsync().AsTask().GetAwaiter().GetResult();

        var result = new
        {
            action = "loader.set",
            key = instance.Key,
            oldLoader,
            loader = settings.Loader,
            identity = parsed.Identity,
            version = parsed.Version,
        };

        if (output.UseStructuredOutput)
        {
            output.WriteData(result);
        }
        else
        {
            output.WriteKeyValueTable(
                "Loader updated",
                ("Instance", instance.Key),
                ("Old Loader", oldLoader),
                ("New Loader", settings.Loader),
                ("Identity", parsed.Identity),
                ("Version", parsed.Version)
            );
            output.WriteSuccess($"Instance {instance.Key} loader set to {settings.Loader}.");
        }

        return ExitCodes.Success;
    }

    public class Arguments : InstanceArgumentsBase
    {
        [CommandArgument(0, "<LURL>")]
        public required string Loader { get; set; }
    }
}
