using Spectre.Console.Cli;
using TridentCore.Cli.Operations;
using TridentCore.Cli.Services;
using TridentCore.Core.Services;

namespace TridentCore.Cli.Commands.Package.Version;

public class PackageVersionSetCommand(
    InstanceContextResolver resolver,
    ProfileManager profileManager,
    CliOutput output
) : InstanceCommandBase<PackageVersionSetCommand.Arguments>(resolver)
{
    protected override int Execute(
        CommandContext context,
        Arguments settings,
        CancellationToken cancellationToken
    )
    {
        var instance = ResolveInstance(settings);
        var result = PackageOperation.VersionSet(resolver, profileManager, settings.Purl, instance.Key, settings.Profile);

        if (output.UseStructuredOutput)
        {
            output.WriteData(new { action = "package.version.set", key = result.Key, oldPurl = result.OldPurl, purl = result.NewPurl });
        }
        else
        {
            output.WriteKeyValueTable(
                "Package version updated",
                ("Instance", result.Key),
                ("Old PURL", result.OldPurl),
                ("New PURL", result.NewPurl)
            );
            output.WriteSuccess($"Package version updated to {result.NewPurl}.");
        }

        return ExitCodes.Success;
    }

    public class Arguments : InstanceArgumentsBase
    {
        [CommandArgument(0, "<VERSION_PURL>")]
        public required string Purl { get; set; }
    }
}
