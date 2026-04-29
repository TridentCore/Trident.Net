using Spectre.Console.Cli;
using Trident.Cli.Services;
using Trident.Core.Services;

namespace Trident.Cli.Commands.Package;

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
