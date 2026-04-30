using Spectre.Console.Cli;
using TridentCore.Cli.Services;
using TridentCore.Core.Services;

namespace TridentCore.Cli.Commands.Package;

public class PackageDisableCommand(
    InstanceContextResolver resolver,
    ProfileManager profileManager,
    CliOutput output
) : PackageEnableCommand(resolver, profileManager, output)
{
    protected override int Execute(
        CommandContext context,
        Arguments settings,
        CancellationToken cancellationToken
    ) => SetEnabled(settings, false);
}
