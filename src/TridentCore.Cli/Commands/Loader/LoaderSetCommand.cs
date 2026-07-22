using Spectre.Console.Cli;
using TridentCore.Cli.Operations;
using TridentCore.Cli.Services;
using TridentCore.Core.Services;

namespace TridentCore.Cli.Commands.Loader;

public class LoaderSetCommand(InstanceContextResolver resolver, ProfileManager profileManager, CliOutput output)
    : InstanceCommandBase<LoaderSetCommand.Arguments>(resolver)
{
    protected override int Execute(CommandContext context, Arguments settings, CancellationToken cancellationToken)
    {
        var instance = ResolveInstance(settings);
        var result = LoaderOperation.Set(Resolver, profileManager, settings.Loader, instance.Key, settings.Profile);

        if (output.UseStructuredOutput)
        {
            output.WriteData(new
            {
                action = "loader.set",
                key = result.Key,
                oldLoader = result.OldLoader,
                loader = result.Loader,
                identity = result.Identity,
                version = result.Version
            });
        }
        else
        {
            output.WriteKeyValueTable("Loader updated",
                                      ("Instance", result.Key),
                                      ("Old Loader", result.OldLoader),
                                      ("New Loader", result.Loader),
                                      ("Identity", result.Identity),
                                      ("Version", result.Version));
            output.WriteSuccess($"Instance {result.Key} loader set to {result.Loader}.");
        }

        return ExitCodes.SUCCESS;
    }

    public class Arguments : InstanceArgumentsBase
    {
        [CommandArgument(0, "<LURL>")]
        public required string Loader { get; set; }
    }
}
