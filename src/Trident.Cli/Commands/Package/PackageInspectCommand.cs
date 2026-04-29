using Spectre.Console.Cli;
using Trident.Cli.Services;
using Trident.Core.Services;

namespace Trident.Cli.Commands.Package;

public class PackageInspectCommand(
    InstanceContextResolver resolver,
    RepositoryAgent repositories,
    CliOutput output
) : Command<PackageInspectCommand.Arguments>
{
    protected override int Execute(
        CommandContext context,
        Arguments settings,
        CancellationToken cancellationToken
    )
    {
        InspectAsync(settings).GetAwaiter().GetResult();
        return ExitCodes.Success;
    }

    private async Task InspectAsync(Arguments settings)
    {
        var parsed = PackageCliHelper.ParsePurl(settings.Purl);
        ResolvedInstanceContext? instance = null;
        LocalPackageDto? local = null;
        if (resolver.TryResolve(settings.Instance, settings.Profile, out var resolved))
        {
            instance = resolved;
            local = PackageDtos.FromEntry(PackageCliHelper.FindEntry(resolved.Profile, settings.Purl));
        }

        var filter = PackageCliHelper.BuildFilter(settings.GameVersion, settings.Loader, settings.ParsedKind, instance);
        var package = await repositories
            .ResolveAsync(parsed.Label, parsed.Namespace, parsed.Pid, parsed.Vid, filter)
            .ConfigureAwait(false);

        output.WriteData(new { key = instance?.Key, local, package = PackageDtos.FromPackage(package) });
    }

    public class Arguments : PackageFilterSettings
    {
        [CommandArgument(0, "<PURL>")]
        public required string Purl { get; set; }
    }
}
