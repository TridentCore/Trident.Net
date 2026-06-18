using Spectre.Console.Cli;
using TridentCore.Cli.Operations;
using TridentCore.Cli.Services;
using TridentCore.Cli.Utilities;
using TridentCore.Core.Services;

namespace TridentCore.Cli.Commands.Package;

public class PackageEnableCommand(
    InstanceContextResolver resolver,
    ProfileManager profileManager,
    CliOutput output
) : InstanceCommandBase<PackageEnableCommand.Arguments>(resolver)
{
    protected override int Execute(
        CommandContext context,
        Arguments settings,
        CancellationToken cancellationToken
    )
    {
        var result = PackageOperation.SetEnabled(Resolver, profileManager, settings.Purl, settings.Instance!, true, settings.Profile);

        if (output.UseStructuredOutput)
        {
            output.WriteData(result);
        }
        else
        {
            output.WriteKeyValueTable(
                "Package enabled",
                ("Instance", result.Key),
                ("PURL", result.Purl),
                ("State", "enabled")
            );
            output.WriteSuccess($"Package {result.Purl} enabled.");
        }

        return ExitCodes.SUCCESS;
    }

    public class Arguments : InstanceArgumentsBase
    {
        [CommandArgument(0, "<PURL>")]
        public required string Purl { get; set; }
    }
}
