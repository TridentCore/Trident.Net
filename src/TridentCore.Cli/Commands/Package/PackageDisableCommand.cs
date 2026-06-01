using Spectre.Console.Cli;
using TridentCore.Cli.Operations;
using TridentCore.Cli.Services;
using TridentCore.Core.Services;

namespace TridentCore.Cli.Commands.Package;

public class PackageDisableCommand(
    InstanceContextResolver resolver,
    ProfileManager profileManager,
    CliOutput output
) : InstanceCommandBase<PackageDisableCommand.Arguments>(resolver)
{
    protected override int Execute(
        CommandContext context,
        Arguments settings,
        CancellationToken cancellationToken
    )
    {
        var result = PackageOperation.SetEnabled(resolver, profileManager, settings.Purl, settings.Instance!, false, settings.Profile);

        if (output.UseStructuredOutput)
        {
            output.WriteData(result);
        }
        else
        {
            output.WriteKeyValueTable(
                "Package disabled",
                ("Instance", result.Key),
                ("PURL", result.Purl),
                ("State", "disabled")
            );
            output.WriteSuccess($"Package {result.Purl} disabled.");
        }

        return ExitCodes.Success;
    }

    public class Arguments : InstanceArgumentsBase
    {
        [CommandArgument(0, "<PURL>")]
        public required string Purl { get; set; }
    }
}
