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
        var result = PackageOperation.VersionSet(Resolver, profileManager, settings.Pref, instance.Key, settings.Profile);

        if (output.UseStructuredOutput)
        {
            output.WriteData(new { action = "package.version.set", key = result.Key, oldPref = result.OldPref, pref = result.NewPref });
        }
        else
        {
            output.WriteKeyValueTable(
                "Package version updated",
                ("Instance", result.Key),
                ("Old PREF", result.OldPref),
                ("New PREF", result.NewPref)
            );
            output.WriteSuccess($"Package version updated to {result.NewPref}.");
        }

        return ExitCodes.SUCCESS;
    }

    public class Arguments : InstanceArgumentsBase
    {
        [CommandArgument(0, "<VERSION_PREF>")]
        public required string Pref { get; set; }
    }
}
